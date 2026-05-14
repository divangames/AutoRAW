' Double-click if Run-AutoRAW.cmd window closes too fast.
' Opens cmd /k in the repo folder, then runs Run-AutoRAW.cmd.
Option Explicit
Dim fso, sh, root, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
root = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "cmd.exe /k " & Chr(34) & "cd /d " & Chr(34) & root & Chr(34) & " && Run-AutoRAW.cmd" & Chr(34)
sh.Run cmd, 1, False
