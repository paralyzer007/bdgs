#!/bin/sh
# Builds "BDGS Launcher.exe" (.NET Framework 4.x) with the Mono C# compiler.
# Windows alternative: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe with the same arguments.
cd "$(dirname "$0")"
mcs -sdk:4.5 -target:exe -platform:anycpu -optimize+ -win32icon:bdgs.ico \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll \
    -out:"BDGS Launcher.exe" BDGSLauncher.cs
