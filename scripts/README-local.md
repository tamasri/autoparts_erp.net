# Local Run Scripts

Run from repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-local.ps1
```

Useful options:

- `-SkipDocker`
- `-SkipBuild`
- `-SkipFrontendInstall`

Stop stack:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\stop-local.ps1
```

Stop + clean artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\clean-local.ps1
```

Deep clean:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\clean-local.ps1 -Deep -PruneDocker
```
