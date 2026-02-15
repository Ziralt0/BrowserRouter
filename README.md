# BrowserRouter

Tired of having to manually change the default browser. Only supports Chrome and Brave, Chrome being the fallback.


## Usage

* Manually create a "BrowserRouter" folder inside "C:\Program Files\" and move the .exe there after building
* Opem CMD as Administrator and run "BrowserRouter.exe /register"
* Change the default browser to BrowserRouter
* Profit

Now everytime a url is clicked, it'll open on the currently open browser


### Commands:
```bash 
/help
/register | Adds BrowserRouter to regedit as a browser
/unregister | Removes BrowserRouter from regedit
```

Should be a single-file self-contained build

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:DebugType=None ^
  /p:DebugSymbols=false
```
