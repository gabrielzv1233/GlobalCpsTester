# Low-Level Global CPS Tester

A CPS tester that works no matter where you click on the screen, built in C#.  
You can click inside the app, another window, or even another display.

## Known Issues

- Clicking near the checkboxes can cause CPS updates to lag or freeze at very high CPS.  
  > This is likely due to heavy UI interaction processing when clicking.  
  > This was partially improved by separating the stats display and controls.
- Clicking inside the app can produce higher CPS readings  
  > When clicking inside the window, CPS may read higher than when clicking outside.  
  > Below a certain threshold (~825 CPS for me), both values should be nearly identical.  
  > This is because windows limitations on detecting inputs outside of a window as far as I can tell.  

## Building the executable

Single executable
```powershell
dotnet publish .\GlobalCpsTester\GlobalCpsTester.csproj -c Release -r win-x64 --self-contained true
```

Unbundled executable
```powershell
dotnet publish .\GlobalCpsTester\GlobalCpsTester.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```