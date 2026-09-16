' ===========================================================================
'  Floppy Launcher - unsichtbarer Start
' ===========================================================================
'  Startet FloppyLauncher.ps1 OHNE aufblitzendes Konsolenfenster.
'  "powershell -WindowStyle Hidden" blitzt kurz auf, dieser Umweg nicht.
'
'  Die Datei findet ihren eigenen Ordner selbst - sie funktioniert also in
'  JEDEM Installationsverzeichnis, ohne dass etwas angepasst werden muss.
'
'  Wird vom Setup als Autostart-Eintrag verwendet:
'      wscript.exe "<Installationsordner>\StartLauncherHidden.vbs"
' ===========================================================================

Option Explicit

Dim fso, shell, here, ps1, cmd
Set fso   = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

here = fso.GetParentFolderName(WScript.ScriptFullName)
ps1  = here & "\FloppyLauncher.ps1"

If Not fso.FileExists(ps1) Then
    MsgBox "FloppyLauncher.ps1 wurde nicht gefunden:" & vbCrLf & ps1, _
           vbCritical, "Floppy Launcher"
    WScript.Quit 1
End If

cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & ps1 & """"

' 0 = Fenster verstecken, False = nicht auf das Ende warten
shell.Run cmd, 0, False
