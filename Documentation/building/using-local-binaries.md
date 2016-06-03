# Using Local .NET Core Binaries in a Console App

Read this document to find out how to use local builds of the coreclr/corefx repos in a console app. You may want to do this for the purposes of performance testing, viewing JIT output, etc. (If you are making performance changes, it could be wise to build the .NET Core runtime/libraries from scratch, instead of copy-and-pasting your code into a new .NET Framework-based app in Visual Studio. This way, any changes made in the .NET Core stack not ported to the full framework will get picked up. Still, if you don't think applies to your use case, feel free to go ahead and do just that.)

## Instructions

- Install the [.NET CLI](http://microsoft.com/net/core). We'll be using this to build our app.

- Perform a build of the corefx repo. See the [Windows](windows-instructions.md) or [Unix](unix-instructions.md) instructions for how to do this, depending on your OS.

  - If you're doing this to measure perf changes, it may be wise to do a release build of the libraries. You can do this on Windows via `build /p:ConfigurationGroup=Release`, or `./build.sh release` on Unix.

- Build the coreclr repo; refer to the [building documentation](https://github.com/dotnet/coreclr/tree/master/Documentation/building) there for how to do this.

  - If you're doing this for the purpose of viewing the JIT output for one of your changes, you'll want to do a debug build here, as the JIT can only be configured to dump the generated disassembly under Debug mode. See [this relevant document](https://github.com/dotnet/coreclr/blob/master/Documentation/building/viewing-jit-dumps.md) in CoreCLR for more on this.

- After you're done building both repos, `cd` to where you have your app. Run `dotnet new` to generate a new project template.

```shell
cd /path/to/testapp
dotnet new
```

This will create two files: `Program.cs` and `project.json`. The former contains the code/logic behind our app, and the `project.json` is used to configure project-related settings such as dependency management and build options. We'll look more into this later.

- Add a `NuGet.config` file to your app's root. Copy-and-paste the following into it (modify the paths to CoreFX and CoreCLR accordingly):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="corefx.local" value="path\to\corefx\bin\packages\<config>" />
    <add key="coreclr.local" value="path\to\coreclr\bin\Product\<OS>.<arch>.<config>\.nuget\pkg" />
  </packageSources>
</configuration>
```

During `dotnet restore`, this will tell the CLI to search those paths for the packages your app will use.

- Modify your `project.json` so it uses the newer versions of the .NET Core binaries.

  - Remove the `Microsoft.NETCore.App` package. This aggregates 3 things into one: the host (`Microsoft.NETCore.DotNetHostPolicy`), the runtime (`Microsoft.NETCore.Runtime.CoreCLR`), and the libraries (`NETStandard.Library`). We want to use the RC2 version of the host, while using newer versions of the other two.

    - See more on this here: dotnet/cli#3290

Your project file should look something like this:

```json
{
  "buildOptions": {
    "emitEntryPoint": true
  },
  "dependencies": {
    "Microsoft.NETCore.DotNetHostPolicy": "1.0.1-rc2-*",
    "Microsoft.NETCore.Runtime.CoreCLR": "1.0.2-rc4-*",
    "NETStandard.Library": "1.5.0-rc4-*"
  },
  "frameworks": {
    "netcoreapp1.0": {
      "imports": [
        "dnxcore50",
        "portable-net45+win8"
      ]
    }
  }
}
```

- Modify `Program.cs` (and possibly add new files) to your heart's content.

- Run `dotnet restore` to restore all of the relevant packages for your app.

- Run `dotnet build` to (finally) build your app! You should see the compiled binary outputted in `bin/config/framework/`. Run your app if you wish.

Congratulations, you've built and run a .NET Core app (almost) entirely from scratch! :)
