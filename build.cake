#tool nuget:?package=vswhere

var target = Argument("target", "Default");
var testFailed = false;
var solutionDir = System.IO.Directory.GetCurrentDirectory();

var testResultDir = Argument("testResultDir", System.IO.Path.Combine(solutionDir, "test-results"));     // ./build.sh --target publish -testResultsDir="somedir"
var artifactDir = Argument("artifactDir", "./artifacts"); 												// ./build.sh --target publish -artifactDir="somedir"
var buildNumber = Argument<int>("buildNumber", 0); 														// ./build.sh --target publish -buildNumber=5

Information("Solution Directory: {0}", solutionDir);
Information("Test Results Directory: {0}", testResultDir);

var peNetProj = System.IO.Path.Combine(solutionDir, "src", "PeNet", "PeNet.csproj");
var peNetTestProj = System.IO.Path.Combine(solutionDir, "test", "PeNet.Test", "PeNet.Test.csproj");

DirectoryPath vsLatest  = VSWhereLatest();
FilePath msBuildPathX64 = (vsLatest==null)
                            ? null
                            : vsLatest.CombineWithFilePath("./MSBuild/15.0/Bin/amd64/MSBuild.exe");


Task("Clean")
	.Does(() =>
	{
		if(DirectoryExists(testResultDir))
			DeleteDirectory(testResultDir, recursive:true);

		if(DirectoryExists(artifactDir))
			DeleteDirectory(artifactDir, recursive:true);

		var binDirs = GetDirectories("./**/bin");
		var objDirs = GetDirectories("./**/obj");
		var testResDirs = GetDirectories("./**/TestResults");
		
		DeleteDirectories(binDirs, true);
		DeleteDirectories(objDirs, true);
		DeleteDirectories(testResDirs, true);
	});


Task("PrepareDirectories")
	.Does(() =>
	{
		EnsureDirectoryExists(testResultDir);
		EnsureDirectoryExists(artifactDir);
	});


Task("Restore")
	.Does(() =>
	{
		DotNetCoreRestore();	  
	});

Task("Build")
	.Does(() =>
	{
		MSBuild(solutionDir, new MSBuildSettings {
			ToolPath = msBuildPathX64,
			Configuration = "Release"
		});
	});


Task("Test")
	.IsDependentOn("Build")
	.ContinueOnError()
	.Does(() =>
	{
		var tests = GetTestProjectFiles();
		
		foreach(var test in tests)
		{
			var projectFolder = System.IO.Path.GetDirectoryName(test.FullPath);

			try
			{
				DotNetCoreTest(test.FullPath, new DotNetCoreTestSettings
				{
					ArgumentCustomization = args => args.Append("-l trx"),
					WorkingDirectory = projectFolder
				});
			}
			catch(Exception e)
			{
				testFailed = true;
				Error(e.Message.ToString());
			}
		}

		// Copy test result files.
		var tmpTestResultFiles = GetFiles("./**/*.trx");
		CopyFiles(tmpTestResultFiles, testResultDir);
	});


Task("Pack")
	.IsDependentOn("Test")
	.Does(() =>
	{
		if(testFailed)
		{
			Information("Do not pack because tests failed");
			return;
		}

		var projects = GetSrcProjectFiles();
		var settings = new DotNetCorePackSettings
		{
			Configuration = "Release",
			OutputDirectory = artifactDir
		};
		
		foreach(var project in projects)
		{
			Information("Pack {0}", project.FullPath);
			DotNetCorePack(project.FullPath, settings);
		}
	});


Task("Publish")
	.IsDependentOn("Test")
	.Does(() =>
	{
		if(testFailed)
		{
			Information("Do not publish because tests failed");
			return;
		}
		var projects = GetFiles("./src/**/*.csproj");

		foreach(var project in projects)
		{
			var projectDir = System.IO.Path.GetDirectoryName(project.FullPath);
			var projectName = new System.IO.DirectoryInfo(projectDir).Name;
			var outputDir = System.IO.Path.Combine(artifactDir, projectName);
			EnsureDirectoryExists(outputDir);

			Information("Publish {0} to {1}", projectName, outputDir);

			var settings = new DotNetCorePublishSettings
			{
				OutputDirectory = outputDir,
				Configuration = "Release"
			};

			DotNetCorePublish(project.FullPath, settings);
		}
	});

Task("Default")
	.IsDependentOn("Test")
	.Does(() =>
	{
		Information("Build and test the whole solution.");
		Information("To pack (nuget) the application use the cake build argument: --target Pack");
		Information("To publish (to run it somewhere else) the application use the cake build argument: --target Publish");
	});


FilePathCollection GetSrcProjectFiles()
{
	return GetFiles("./src/**/*.csproj");
}

FilePathCollection GetTestProjectFiles()
{
	return GetFiles("./test/**/*Test/*.csproj");
}

string GetMSBuildExePath()
{
	var msbuildPath = (string) Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\software\Microsoft\MSBuild\ToolsVersions\4.0", "MSBuildToolsPath", null);

	if(msbuildPath == null)
		throw new Exception($"Could not find msbuild path.");

	var msbuildExe = System.IO.Path.Combine(msbuildPath, "msbuild.exe");

	if(!System.IO.File.Exists(msbuildExe))
		throw new Exception($"Could not find msbuild exe.");

	Information($"Found msbuild.exe at {msbuildExe}");
	return msbuildExe;
}

RunTarget(target);