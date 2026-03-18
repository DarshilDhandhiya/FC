# FC — FileCopy Console Application

A .NET console application that automatically copies project attachment files from their original source locations (`FilePath`) to a new delivery location (`FilePathAbs`), computed from each project's `DeliveryLocationSystem` path. Successfully copied files are recorded in an audit log and the computed destination path is persisted back to the database.

---

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Database Schema](#database-schema)
- [How It Works](#how-it-works)
  - [Step-by-step flow](#step-by-step-flow)
  - [Destination path computation](#destination-path-computation)
- [File Structure](#file-structure)
- [FileCopySettings Reference](#filecopysettings-reference)
- [Exit Codes](#exit-codes)
- [Sample Console Output](#sample-console-output)

---

## Overview

The application processes one or more projects that are flagged for file copy in a central **master database**. For each project it:

1. Connects to that project's own SQL Server database.
2. Finds all attachment records whose absolute destination path (`FilePathAbs`) has not been set yet.
3. Computes `FilePathAbs` by re-rooting the source path under the project's `DeliveryLocationSystem` folder.
4. Creates any missing destination directories, then copies the file.
5. On success, writes the computed `FilePathAbs` back to the attachment record and inserts a row into the audit log table.

---

## Prerequisites

- .NET 6+ runtime
- Access to two SQL Server databases:
  - **Master database** (`ProeTrackNxt_Master`) — holds the project list.
  - **Project database** (one per project, e.g. `ProeTrackNxt_PROJCODE`) — holds attachment records and the copy-log table.
- An `appsettings.json` file in the same directory as the executable (see [Configuration](#configuration)).

---

## Configuration

Create `appsettings.json` next to the compiled executable:

```json
{
  "ConnectionStrings": {
    "MasterConnection": "Server=YOUR_SERVER;Database=ProeTrackNxt_Master;Integrated Security=True;",
    "ProjectConnection": "Server=SQL_INST;Database=ProeTrackNxt_####;Integrated Security=True;"
  },
  "FileCopySettings": {
    "MaxFileSizeBytes": 0,
    "MaxRetryAttempts": 3,
    "RetryDelayMs": 1000,
    "EnableDetailedLogging": true,
    "BufferSizeBytes": 81920
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

**Connection string placeholders** in `ProjectConnection` are replaced at runtime for each project:

| Placeholder | Replaced with |
|-------------|---------------|
| `####`      | `ProjectCode` column value from the master `Projects` table |
| `SQL_INST`  | `Instance` column value from the master `Projects` table |

---

## Database Schema

### Master database — `[ProeTrackNxt_Master].[dbo].[Projects]`

| Column | Type | Description |
|--------|------|-------------|
| `ProjectCode` | `nvarchar` | Unique project identifier; used to build the project DB connection string |
| `Instance` | `nvarchar` | SQL Server instance name for the project database |
| `IsFileCopyRequired` | `bit` | Only projects where this is `1` are processed |
| `DeliveryLocationSystem` | `nvarchar` | Root delivery path on the target system, e.g. `C:\Onshore-proposal\ProposalMaster\` |

### Project database — `[dbo].[TransEngChk_T_Att]`

| Column | Type | Description |
|--------|------|-------------|
| `ID` | `int` | Primary key |
| `FilePath` | `nvarchar` | Source file path (original location of the file) |
| `FilePathAbs` | `nvarchar` | Computed absolute destination path; `NULL` or empty until the file is successfully copied |
| `SystemFileName` | `nvarchar` | Display name of the file |
| `CreatedDate` | `datetime` | Record creation timestamp |

Only rows where `FilePathAbs IS NULL OR FilePathAbs = ''` are picked up for processing.

### Project database — `[dbo].[Tab_CopyLog]`

Audit log inserted after every copy attempt (success or failure).

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `int` | Auto-increment primary key |
| `FileName` | `nvarchar` | File name |
| `SourcePath` | `nvarchar` | Source path used |
| `DestinationPath` | `nvarchar` | Destination path used |
| `OverwriteFlag` | `bit` | Whether overwrite was enabled |
| `StartTime` | `datetime` | When the copy attempt started (UTC) |
| `EndTime` | `datetime` | When the copy attempt finished (UTC) |
| `Status` | `nvarchar` | `Success` or `Failed` |
| `ErrorDetails` | `nvarchar` | Error message if the copy failed |
| `FileSizeBytes` | `bigint` | File size in bytes |

---

## How It Works

### Step-by-step flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Startup                                                                │
│  Load appsettings.json → read MasterConnection, ProjectConnection,      │
│  FileCopySettings                                                       │
└──────────────────────────────────┬──────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Query master DB                                                        │
│  SELECT ProjectCode, Instance, DeliveryLocationSystem                   │
│  FROM [Projects] WHERE IsFileCopyRequired = 1                           │
└──────────────────────────────────┬──────────────────────────────────────┘
                                   │  (one iteration per project)
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  For each project                                                       │
│  Build project DB connection string                                     │
│  (replace #### → ProjectCode, SQL_INST → Instance)                     │
└──────────────────────────────────┬──────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Query project DB                                                       │
│  SELECT * FROM TransEngChk_T_Att                                        │
│  WHERE FilePathAbs IS NULL OR FilePathAbs = ''                          │
└──────────────────────────────────┬──────────────────────────────────────┘
                                   │  (one iteration per attachment)
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  For each pending attachment                                            │
│  1. Compute FilePathAbs from FilePath + DeliveryLocationSystem          │
│  2. Verify source file exists                                           │
│  3. Check file size limit (if configured)                               │
│  4. Create destination directory if missing                             │
│  5. Copy file (with up to MaxRetryAttempts retries)                     │
│  6. On success → UPDATE TransEngChk_T_Att SET FilePathAbs = computed    │
│  7. INSERT row into Tab_CopyLog (success or failure)                    │
└─────────────────────────────────────────────────────────────────────────┘
```

### Destination path computation

`FilePathAbs` is built by finding the shared anchor folder between `DeliveryLocationSystem` and `FilePath`, then re-rooting everything after that anchor under the delivery base.

**Algorithm:**

1. Strip trailing slashes from `DeliveryLocationSystem` → `deliveryBase`.
2. Take the last path segment of `deliveryBase` as the **match anchor** (e.g. `ProposalMaster`).
3. Split `FilePath` into path segments.
4. Find the **last** occurrence of the match anchor (case-insensitive) among those segments.
5. Take every segment **after** the matched anchor as the relative sub-path.
6. `FilePathAbs = Path.Combine(deliveryBase, relativePath)`

**Example:**

```
FilePath               = D:\ProposalMaster\02 Delivery\07 IN\01-ITB\file.ppt
DeliveryLocationSystem = C:\Onshore-proposal\ProposalMaster\

deliveryBase  = C:\Onshore-proposal\ProposalMaster
matchAnchor   = ProposalMaster
                        ↑ found at index 1 in FilePath segments
relativePath  = 02 Delivery\07 IN\01-ITB\file.ppt

FilePathAbs   = C:\Onshore-proposal\ProposalMaster\02 Delivery\07 IN\01-ITB\file.ppt
```

> **Note:** If the anchor folder name appears more than once in `FilePath`, the **last** occurrence is used so that a deep project sub-folder named the same as the root anchor is handled correctly.
>
> If no match is found, the attachment is marked `Failed` and no file copy is attempted.

---

## File Structure

```
FC/
├── Program.cs                 # Entry point; iterates over projects
├── FileCopyService.cs         # Core copy logic, path computation
├── FileCopySettings.cs        # Configuration model (MaxRetryAttempts, etc.)
├── Attachment.cs              # Model for TransEngChk_T_Att rows
├── AttachmentRepository.cs    # DB read/write for TransEngChk_T_Att
├── CopyLog.cs                 # Model for Tab_CopyLog rows
├── CopyLogRepository.cs       # DB read/write for Tab_CopyLog
├── DataAccessHelper.cs        # Low-level ADO.NET helpers
└── appsettings.json           # Connection strings and settings (not in source control)
```

---

## FileCopySettings Reference

Configured under the `FileCopySettings` key in `appsettings.json`.

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxFileSizeBytes` | `0` | Maximum file size allowed for copy in bytes. `0` means unlimited. |
| `MaxRetryAttempts` | `3` | Number of additional retry attempts if a copy fails. |
| `RetryDelayMs` | `1000` | Milliseconds to wait between retry attempts. |
| `EnableDetailedLogging` | `true` | Enables verbose log output. |
| `BufferSizeBytes` | `81920` | Internal I/O buffer size (80 KB). |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0`  | All files copied successfully (or no work to do). |
| `1`  | One or more copy operations failed. |
| `-1` | Fatal startup error (e.g. missing config, cannot connect to master DB). |

---

## Sample Console Output

```
========================================
  FileCopy Console Application
  Started at: 2026-03-17 10:00:00
========================================

Found 1 project(s) to process.

========================================
  Processing Project: PROJ01
========================================
  Found 2 attachment(s) to process.

  Project PROJ01 Summary:
    Total Processed: 2
    Successful:      1
    Failed:          1

    [OK]   file_example_PPT_250kB.ppt
           Source: D:\ProposalMaster\02 Delivery\07 IN\01-ITB\file_example_PPT_250kB.ppt
           Dest:   C:\Onshore-proposal\ProposalMaster\02 Delivery\07 IN\01-ITB\file_example_PPT_250kB.ppt
           Size:   244.14 KB
           Duration: 0.03 seconds

    [FAIL] missing_file.docx
           Source: D:\ProposalMaster\02 Delivery\missing_file.docx
           Dest:   C:\Onshore-proposal\ProposalMaster\02 Delivery\missing_file.docx
           Error:  Source file not found: D:\ProposalMaster\02 Delivery\missing_file.docx

========================================
  Overall Processing Summary
========================================
  Projects Processed: 1
  Total Successful:   1
  Total Failed:       1

========================================
  Completed at: 2026-03-17 10:00:01
========================================
```