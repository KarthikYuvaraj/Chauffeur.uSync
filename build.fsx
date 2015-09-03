#I @"tools/FAKE/tools/"
#r @"FakeLib.dll"

open Fake

let authors = ["Aaron Powell"]

let chauffeurDir = "./Chauffeur/bin/"
let chauffeurRunnerDir = "./Chauffeur.Runner/bin/"
let packagingRoot = "./packaging/"
let packagingDir = packagingRoot @@ "chauffeur"
let packagingRunnerDir = packagingRoot @@ "chauffeur.runner"

let buildMode = getBuildParamOrDefault "buildMode" "Release"

let isAppVeyorBuild = environVar "APPVEYOR" <> null

open Fake.AssemblyInfoFile

let projectName = "Chauffeur"
let chauffeurSummary = "Chauffeur is a tool for helping with delivering changes to an Umbraco instance."
let chauffeurDescription = chauffeurSummary

let chauffeurRunnerSummary = "Chauffeur Runner is a CLI for executing Chauffeur deliverables against an Umbraco instance."
let chauffeurRunnerDescription = chauffeurRunnerSummary

let releaseNotes = 
    ReadFile "ReleaseNotes.md"
        |> ReleaseNotesHelper.parseReleaseNotes

let prv = match releaseNotes.SemVer.PreRelease with
    | Some pr -> sprintf "-%s%s" pr.Name (if environVar "APPVEYOR_BUILD_NUMBER" <> null then environVar "APPVEYOR_BUILD_NUMBER" else "")
    | None -> ""

let nugetVersion = sprintf "%d.%d.%d%s" releaseNotes.SemVer.Major releaseNotes.SemVer.Minor releaseNotes.SemVer.Patch prv

Target "Default" DoNothing

Target "AssemblyInfo" (fun _ ->
    CreateCSharpAssemblyInfo "SolutionInfo.cs"
      [ Attribute.Product projectName
        Attribute.Version releaseNotes.AssemblyVersion
        Attribute.FileVersion releaseNotes.AssemblyVersion
        Attribute.ComVisible false ]
)

Target "Clean" (fun _ ->
    CleanDirs [chauffeurDir; chauffeurRunnerDir]
)

Target "RestoreChauffeurPackages" (fun _ ->
    RestorePackage (fun p -> p) "./Chauffeur/packages.config"
)

Target "RestoreChauffeurDemoPackages" (fun _ ->
    RestorePackage (fun p -> p) "./Chauffeur.Demo/packages.config"
)

Target "RestoreChauffeurTestsPackages" (fun _ ->
    RestorePackage (fun p -> p) "./Chauffeur.Tests/packages.config"
)

Target "BuildApp" (fun _ ->
    MSBuild null "Build" ["Configuration", buildMode] ["Chauffeur.sln"]
    |> Log "AppBuild-Output: "
)

Target "UnitTests" (fun _ ->
    !! (sprintf "./Chauffeur.Tests/bin/%s/**/Chauffeur.Tests*.dll" buildMode)
    |> NUnitParallel (fun p -> 
            {p with 
                DisableShadowCopy = true;
                OutputFile = (sprintf "./Chauffeur.Tests/bin/%s/TestResults.xml" buildMode) })
)

Target "CreateChauffeurPackage" (fun _ ->
    let libDir = packagingDir @@ "lib/net45/"
    CleanDirs [libDir]

    CopyFile libDir (chauffeurDir @@ "Release/Chauffeur.dll")
    CopyFiles packagingDir ["LICENSE.md"; "README.md"]


    NuGet (fun p -> 
        {p with
            Authors = authors
            Project = projectName
            Description = chauffeurDescription
            OutputPath = packagingRoot
            Summary = chauffeurSummary
            WorkingDir = packagingDir
            Version = nugetVersion
            ReleaseNotes = toLines releaseNotes.Notes
            SymbolPackage = NugetSymbolPackage.Nuspec
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Dependencies =
                ["System.IO.Abstractions", "1.4.0.83"]
            Publish = hasBuildParam "nugetkey" }) "Chauffeur/Chauffeur.nuspec"
)

Target "CreateRunnerPackage" (fun _ ->
    let libDir = packagingRunnerDir @@ "lib/net45/"
    CleanDirs [libDir]

    CopyFile libDir (chauffeurRunnerDir @@ "Release/Chauffeur.Runner.exe")
    CopyFiles packagingDir ["LICENSE.md"; "README.md"]


    NuGet (fun p -> 
        {p with
            Authors = authors
            Project = projectName
            Description = chauffeurRunnerDescription
            OutputPath = packagingRoot
            Summary = chauffeurRunnerSummary
            WorkingDir = packagingRunnerDir
            Version = nugetVersion
            ReleaseNotes = toLines releaseNotes.Notes
            SymbolPackage = NugetSymbolPackage.Nuspec
            Dependencies =
                ["Chauffeur", NormalizeVersion nugetVersion]
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" }) "Chauffeur.Runner/Chauffeur.Runner.nuspec"
)

Target "BuildVersion" (fun _ ->
    Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" nugetVersion) |> ignore
)

"Clean"
    =?> ("BuildVersion", isAppVeyorBuild)
    ==> "RestoreChauffeurPackages"
    =?> ("RestoreChauffeurDemoPackages", buildMode <> "Release")
    ==> "RestoreChauffeurTestsPackages"
    ==> "BuildApp"
    ==> "UnitTests"
    ==> "CreateChauffeurPackage"
    ==> "CreateRunnerPackage"
    ==> "Default"

RunTargetOrDefault "Default"