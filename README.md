# WebPageDownloader

A .NET console application that downloads many web pages concurrently and saves them to disk. It is designed to handle hundreds to thousands of URLs with constant memory use, and it never lets one failing URL stop the rest of the run.

## Features

- **Bounded concurrency**: a configurable number of downloads run in parallel (`Parallel.ForEachAsync`), so the target servers and the local machine are not overwhelmed.
- **Streaming to disk**: each response body is copied to a file in small chunks. A page is never held in memory as a whole, so memory use does not grow with page size or URL count.
- **Safe writes**: a page is written to a temporary file and renamed to its final name only when the download completed. A cancelled or failed download never leaves a half-written file that looks valid.
- **Per-URL failure handling**: network errors, timeouts, non-success HTTP status codes and I/O errors are recorded for that URL only. All other URLs continue.
- **Cancellation**: Ctrl+C cancels the run cleanly and removes temporary files of in-progress downloads.
- **Manifest**: every URL gets one line in `results.jsonl` (outcome, status code, file path, size, error), so results can be audited or post-processed.
- **Configurable**: concurrency, request timeout and output folder come from `appsettings.json` and can be overridden on the command line.

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or later

## Getting started

```bash
git clone https://github.com/arabelageorgianafoanene/WebPageDownloader.git
cd WebPageDownloader

dotnet build
dotnet run --project WebPageDownloader -- urls.txt
```

`urls.txt` is a plain text file with one absolute `http`/`https` URL per line:

```
https://example.com/
https://www.wikipedia.org/
https://dotnet.microsoft.com/
```

Duplicate URLs are downloaded once. Invalid URLs are reported as failed results and do not stop the run.

## Output

By default everything is written to the `output` folder:

```
output/
├── 3A7F9C21B4E05D....html     # one file per successfully downloaded URL
├── 9B12E7F0A3C4D1....html
└── manifest.json              # one JSON entry per URL
```

Example manifest lines:

```json
{"Url":"https://example.com/","IsSuccess":true,"StatusCode":200,"FilePath":"output/3A7F9C21B4E05D....html","Bytes":48213,"Error":null}
{"Url":"https://example.com/missing","IsSuccess":false,"StatusCode":404,"FilePath":null,"Bytes":null,"Error":"HTTP 404 (Not Found)"}
```

File names are the SHA-256 hash of the URL. The same URL always maps to the same file, so re-running the program overwrites earlier results instead of creating duplicates. The manifest is the lookup from URL to file.

## Configuration

Settings live in `appsettings.json` (section `Downloader`):

```json
{
  "Downloader": {
    "MaxConcurrency": 20,
    "RequestTimeoutSeconds": 30,
    "OutputDirectory": "output"
  }
}
```

| Setting | Default | Valid range | Description |
|---|---|---|---|
| `MaxConcurrency` | 10 | 1-200 | Number of downloads running at the same time. This limits parallelism only. The number of URLs is not limited. |
| `RequestTimeoutSeconds` | 30 | 1-300 | Timeout per HTTP request. |
| `OutputDirectory` | `output` | - | Folder for downloaded pages and the manifest. |

Values are validated at startup, and the program fails fast with a clear message if a setting is invalid.

Command-line arguments override the file:

```bash
dotnet run --project WebPageDownloader -- urls.txt --Downloader:MaxConcurrency=50 --Downloader:OutputDirectory=C:\temp\pages
```

Precedence: command line, then environment variables, then `appsettings.json`, then the defaults in code.

## Design

| Concern | Decision | Why |
|---|---|---|
| Concurrency | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` | Built-in throttling and cancellation. Does not create a task per URL up front, so it scales to large inputs. |
| Memory | Stream response bodies to disk (`ResponseHeadersRead` + `CopyToAsync`) | Memory per download stays at roughly one 80 KB buffer, regardless of page size. |
| Persistence | Files on disk plus a JSON Lines manifest | Pages are large, write-once blobs. Files fit that naturally and need no extra dependencies. JSON Lines is append-friendly and stays valid after a crash. |
| Storage abstraction | `IPageStore` interface, `FileSystemPageStore` implementation | The downloader does not know about the disk, so a database-backed store can be added without touching the download logic. |
| Failures | Every non-cancellation exception becomes a failed `DownloadResult` | `Parallel.ForEachAsync` stops on the first unhandled exception, so failures must be contained per URL. |
| Cancellation | The `CancellationToken` is passed through every async call | Caller cancellation propagates. Timeouts are treated as ordinary failures. |
| Atomic writes | Write to `*.tmp`, then rename | A final file name only ever refers to a complete page. Leftover `.tmp` files from a crashed run are removed at startup. |
| Configuration | `IOptions<DownloaderOptions>` with data annotation validation | One place for settings, validated on startup. |
| HTTP | Typed `HttpClient` via `IHttpClientFactory` | Correct connection pooling and lifetime management. |

### Project layout

```
WebPageDownloader/
├── WebPageDownloader.slnx
└── WebPageDownloader/              # console application
    ├── Configuration/              # DownloaderOptions
    ├── Models/                     # DownloadResult
    ├── Services/                   # IWebPageDownloader and its HTTP implementation
    ├── Storage/                    # IPageStore, FileSystemPageStore
    ├── appsettings.json
    └── Program.cs
```

## Assumptions and limitations

- Saved files use the `.html` extension for every URL. This is a convenience for the "web pages" use case. Other content types (JSON, images, PDF) are saved correctly but with that extension.
- Only the response body of successful (2xx) responses is saved. Other responses are recorded in the manifest with their status code.
- Redirects are followed by `HttpClient` with its default settings.
- No JavaScript is executed. The program saves the HTML the server returns, not the rendered page.
- URLs that differ only in the fragment (`#section`) are treated as different URLs.
- The program does not honour `robots.txt` or per-site rate limits.

## Possible next steps

- **Richer command-line interface with System.CommandLine.** Today the input file is a single
  positional argument that `Program.cs` picks out of the command-line arguments by hand
  That is deliberately small. If the interface grows (more options, subcommands), I would move to
  [System.CommandLine](https://learn.microsoft.com/dotnet/standard/commandline/), Microsoft's library for
  command-line parsing. 