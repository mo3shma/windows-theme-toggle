@echo off
rem Builds ThemeToggle.exe with the C# compiler that ships with Windows (no installs needed).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize /win32icon:ThemeToggle.ico /out:ThemeToggle.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll ThemeToggle.cs
