# Troubleshooting

| Problem | Fix |
|---|---|
| `The framework 'Microsoft.NETCore.App' version '11.0...' was not found` / SDK not found | Install the .NET 11 SDK, or add `-p:SharpTfm=net8.0` to your command |
| NuGet cannot find `Microsoft.AspNetCore.Components.WebAssembly` 11.x | Use the .NET 11 SDK with preview feeds, or build the web part with `-p:SharpTfm=net8.0` |
| Script error on a `union` line | Needs a Roslyn compiler with C# 15; use the .NET 11 SDK (scripts use the SDK's compiler) |
| Status bar says `CPU` | The shader could not be created (old graphics driver). Everything still works, only slower |
| Scene broke after an error | Look in `Backups/`, copy the newest file into `Scenes/` |
| Details of any error | `Logs/errors.log` |
| Old text from an earlier zip is still there | Extract into a fresh folder; `tar -xf` overwrites files but does not delete removed ones |
