# MsOrNUnitToXunitConverter.Mcp

An [MCP](https://modelcontextprotocol.io) server over stdio that exposes the MSTest/NUnit → xUnit conversion as
tools, so a solution can be converted from an MCP client instead of the command line.

## Tools

| Tool | Arguments | Result |
| --- | --- | --- |
| `list_test_projects` | `solutionPath` | The test projects of a solution, their framework, and how many test files each holds. Changes nothing. |
| `convert_solution` | `solutionPath`, `forceMsTest` | Converts every test project in a `.sln` or `.slnx`. |
| `convert_project` | `csprojPath`, `forceMsTest` | Converts a single project, like the command line tool. |
| `restore_backup` | `path` | Restores `../Old/<ProjectName>` over a solution's projects, or over one project. |

Each converted project is backed up to `../Old/<ProjectName>` first, and rolled back if its conversion fails
part way through. A project that fails is reported in the result and the rest of the solution still runs.

Only projects whose csproj references a known test-framework package are touched. That gate matters because the
file-level detectors are substring sniffs: without it, an ordinary library that merely mentions `"[TestFixture]"`
in a string literal would be rewritten.

## Registering it

Registered at user scope, so it is available in every repository:

```bash
msbuild MsOrNUnitToXunitConverter.Mcp/MsOrNUnitToXunitConverter.Mcp.csproj /p:Configuration=Release

claude mcp add --scope user msornunit-to-xunit \
  "<repo>/MsOrNUnitToXunitConverter.Mcp/bin/Release/net10.0/MsOrNUnitToXunitConverter.Mcp.exe"
```

`claude mcp get msornunit-to-xunit` shows the registration; `claude mcp remove msornunit-to-xunit -s user`
removes it.

The registration runs the built executable rather than `dotnet run`, because `dotnet run` writes build output
to stdout and stdout is the JSON-RPC channel. All logging goes to stderr for the same reason — if you add
output of any kind, keep stdout clear. A rebuilt Release binary is picked up on the next session; a
`/t:clean` leaves the server unable to start until Release is built again.
