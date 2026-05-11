@echo off

:loop
	cls
	time /t 0 >NUL
	nvidia-smi.exe
	timeout /t 2 /nobreak >NUL
goto loop
