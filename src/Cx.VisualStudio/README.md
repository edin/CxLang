# CX Visual Studio Extension

Small Visual Studio 2022 extension for `.cx` files.

Current support:

- associates `.cx` files with a CX content type
- highlights keywords, built-in types, strings, chars, numbers, comments, attributes, and declarations after `fn`, `struct`, `enum`, `union`, `interface`, `requires`, `type`, `extension`, `module`, and `test`

Build:

```powershell
dotnet build src\Cx.VisualStudio\Cx.VisualStudio.csproj
```

The `.vsix` is written under `src\Cx.VisualStudio\bin\Debug`.
