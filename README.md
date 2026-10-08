# WebPageDownloader

A .NET console application that reads a list of URLs from a text file, downloads each page in parallel and saves it to disk. Transient failures are retried automatically, and every URL ends up with a recorded result, whether it succeeded or failed.

## Features

- Parallel downloads with a configurable concurrency limit
- Retry with exponential backoff and jitter (Polly), applied to the whole download-and-save operation
- Per-attempt timeout
- Maximum page size, enforced from the `Content-Length` header and again while streaming
- Atomic file writes (temporary file, then move), so a failed attempt never leaves a partial file
- Failures are isolated: one failing URL does not stop the others
- Graceful cancellation with Ctrl+C
- Results are written to a JSON manifest

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 10 (the solution uses the `.slnx` format, which needs SDK 9.0.200 or later)
- A text file with one URL per line

## Getting started

```powershell
git clone https://github.com/arabelageorgianafoanene/WebPageDownloader.git
cd WebPageDownloader
dotnet build
```

## Usage

Run from the repository root (the folder containing the `.slnx`):

```powershell
dotnet run --project WebPageDownloader -- urls.txt
```

The `--` separates the arguments of `dotnet` from those of the application. The first argument after it must be the path to the URLs file. Relative paths are resolved from the folder you run the command in.

### Input file

One URL per line:

```
https://example.com
https://httpbin.org/status/404
https://httpbin.org/status/503
```

### Options

Options use the .NET configuration syntax and go after the file path. They can also be set in `appsettings.json` under the `Downloader` section.

| Option | Description | Default |
|---|---|---|
| `--Downloader:MaxConcurrency` | Number of parallel downloads | see `appsettings.json` |
| `--Downloader:OutputDirectory` | Folder where pages are saved | `output` |
| `--Downloader:RequestTimeoutSeconds` | Timeout for each download attempt | see `appsettings.json` |
| `--Downloader:MaxFileSizeBytes` | Maximum size of a downloaded page, in bytes | `10485760` (10 MB) |

Example:

```powershell
dotnet run --project WebPageDownloader -- urls.txt --Downloader:MaxConcurrency=5 --Downloader:OutputDirectory=output
```

Invalid values (for example a non-positive size limit) are rejected at startup with a list of the problems.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | The run completed. Check the manifest for the result of each URL |
| `1` | Missing argument, file not found, invalid configuration or unhandled error |
| `130` | Cancelled by the user (Ctrl+C) |

## Output

- Each successfully downloaded page is saved in the output directory as the raw response body.
- A JSON manifest records the result for every URL: success or failure, HTTP status code, saved file path, size and error message.
- Only the page itself is saved. Images, CSS and scripts are not downloaded, and pages rendered by JavaScript are saved as the server sent them, so a page may look different from the original when opened locally.

## Reliability

Each URL is downloaded and saved as one unit of work, wrapped by a single Polly resilience pipeline (`downloadAndSave`). A retry therefore repeats the request, the body read and the file write.

| Aspect | Behavior |
|---|---|
| Retries | Up to 3, exponential backoff starting at 1 second, with jitter |
| Retried | Network errors, timeouts, HTTP 408, 429 and 5xx, and I/O errors |
| Not retried | Other 4xx responses (for example 404), pages over the size limit, and cancellation |
| Timeout | Each attempt has its own timeout (`RequestTimeoutSeconds`) |
| After the last attempt | The URL is recorded as failed, with the status code and message, and the run continues |
| Cancellation | Ctrl+C stops the run immediately, without further retries |

### Size limit

A page larger than `MaxFileSizeBytes` is rejected and is not retried. The limit is checked in two places:

1. **Early check:** if the response declares a `Content-Length` above the limit, the download is rejected before any data is read.
2. **Streaming check:** the bytes are counted while the body is copied to disk, because the header can be missing or wrong. The copy stops as soon as the limit is exceeded, and the temporary file is removed.

The streaming check measures the decompressed size, since the HTTP client decompresses responses automatically.

## Design decisions

- **One Polly pipeline around download and save, not the standard HTTP resilience handler.** The handler only covers the HTTP exchange. Here the unit of work includes streaming the body to disk, and a dropped connection or a write error during that step would not be retried by the handler. The pipeline covers all of it, so there is a single retry layer and attempts are not multiplied.
- **No circuit breaker.** One `HttpClient` calls many different hosts. A breaker shared across them would let one failing site block healthy ones.
- **Exceptions drive the retry.** `DownloadPageAsync` calls `EnsureSuccessStatusCode()` and lets exceptions propagate, so Polly can classify them. The failed result is built once, after the retries are exhausted.
- **Results are stored by index.** Each parallel iteration writes to its own slot of a pre-sized array. This is thread-safe without locks and keeps the results in the same order as the input URLs.
- **Polly owns the timeouts.** `HttpClient.Timeout` is infinite, and the pipeline applies the per-attempt timeout.

## Project structure

```
WebPageDownloader.slnx
├─ WebPageDownloader/
│   ├─ Application/      DownloadApplication: orchestrates a run
│   ├─ Configuration/    DownloaderOptions and validation
│   ├─ Extensions/       service registration, including the Polly pipeline
│   ├─ Input/            URL source (reads the URLs file)
│   ├─ Models/           DownloadResult
│   ├─ Services/         WebPageDownloader: parallel download, retry, error handling
│   ├─ Storage/          page store, size-limited stream copy, ResponseTooLargeException
│   └─ Program.cs        argument handling and host setup
└─ WebPageDownloaderTests/
```

## Tests

```powershell
dotnet test
```

## Troubleshooting

- **"Missing argument: path of the file with URLs."** The first argument after `--` must be the file path and must not start with `-`.
- **"File not found."** Relative paths are resolved from the current working directory. Use a full path if in doubt.
- **No retry messages in the log.** Retries are logged at `Warning` level. Make sure the logging configuration does not filter them out.
