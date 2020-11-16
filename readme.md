# Interop .NET with Objective-C

This repository is for investigating interop between .NET and Objective-C. This investigation is done by debugging on macOS and referencing the [Xamarin-macios](https://github.com/xamarin/xamarin-macios) repository.

The current focus is on non-UI Objective-C interaction. This permits focusing on interop fundamentals and understanding the current environment. Given this state the [`MyConsoleApp`](./MyConsoleApp/MyConsoleApp.csproj) should be the primary project of interest.

## Requirements

This is the environment being used by the repository owner.

- .NET 5+
- macOS 15+
- XCode 12.1

### Recommended

- [Visual Studio for Mac](https://visualstudio.microsoft.com/vs/mac/)

## Build/Run

- Build and run using the `dotnet` command.
    - `dotnet build ./MyConsoleApp/MyConsoleApp.csproj`
    - `dotnet run --project ./MyConsoleApp/MyConsoleApp.csproj`

- Build using Visual Studio for Mac.
    - Set `MyConsoleApp` as the "start up" project and press "F5" to build/debug. 

## Debugging

The C# code is debugged using Visual Studio for Mac.

The C code, CoreCLR, and Objective-C runtime are debugged through LLDB from VSCode or command line. A [`launch.json`](./.vscode/launch.json) is included to assist in debugging for both a `Debug` and `Release` build.

Objective-C code debugging has not been performed.

# Resources

[Xamarin-macios repository](https://github.com/xamarin/xamarin-macios).

[Objective-C runtime API](https://developer.apple.com/documentation/objectivec/objective-c_runtime).

[Objective-C runtime programming guide](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Introduction/Introduction.html).