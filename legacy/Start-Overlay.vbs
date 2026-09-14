' Uruchamia nakladke Claude Status Overlay bez okna konsoli.
Option Explicit
Dim fso, sh, here, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh  = CreateObject("WScript.Shell")
here = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & here & "\ClaudeStatusOverlay.ps1"""
sh.Run cmd, 0, False
