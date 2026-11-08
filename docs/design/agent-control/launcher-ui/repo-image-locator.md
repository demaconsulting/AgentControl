### RepoImageLocator

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoImageLocator` finds and decodes the picture a repo offers for its card: `icon.png`, else `logo.png`, in the
repo's root folder. A repo may be untrusted (the user clones whatever they are asked to work on), so the unit's
single responsibility is to turn "a file some repo put in its folder" into either a small bitmap that is safe to
show or no picture at all, without ever reading outside the repo, exhausting memory or throwing. Its behavior is
specified by the unit requirements `AgentControl-RepoImageLocator-FindsPicture`, `-OnlyGenuinePng`, `-SizeLimit`,
`-DimensionLimit`, `-NoLinks`, `-LinkedRoot`, `-StaysInRoot`, `-DecodeToTile`, `-NeverFails` and `-ChangeIdentity`
(each prefixed `AgentControl-RepoImageLocator`), and together with `RepoCardViewModel` (which asks for the picture in
the background and exposes it, `AgentControl-RepoCardViewModel-RepoPicture`) it provides
`AgentControl-LauncherUI-CardIdentityAndStatus`.

#### Data Model

**RepoImageFile** (`readonly record struct`): `Root`, `Path`, `LastWriteTimeUtc`, `Length` - one validated candidate.
It is also the cache key: the same root, path, last-write time and size means the same picture, so a caller can skip
decoding an unchanged file. `Root` is the repo root `Find` validated; it travels with the file so that `Decode` checks
the file against that root again (`AgentControl-RepoImageLocator-StaysInRoot`).

**Limits** (public constants): `MaxFileBytes` = 1 MB (a card icon needs no more, and the cap bounds the memory a
hostile file can use); `MaxDimension` = 4096 pixels (the largest width or height the PNG header may declare, so at
most 4096 x 4096 pixels, 64 MB as a 32-bit bitmap); `TileDecodeWidth` = 96 pixels (twice the 44 pixel tile, so the
picture stays sharp at 200 percent display scaling).

**Candidate names**: `icon.png`, then `logo.png`, matched without regard to letter case.

#### Key Methods

**Find(repoRoot)**: Returns the first acceptable `RepoImageFile`, or `null`.

1. The root must exist and must not itself be a link (a symbolic link, a junction or another reparse point; checked
   by `FileSystemInfo.LinkTarget` and the `ReparsePoint` attribute). **A linked root has no picture**
   (`AgentControl-RepoImageLocator-LinkedRoot`): this is a decision for the picture feature only - other features
   deliberately accept a repo under a linked parent folder, and so does this one; only a root that *is* a link is
   refused, because it lets the repo pick which folder is searched.
2. Only the files directly in the root are listed (never the folders below it), and a file whose name matches a
   candidate (ignoring case) is examined in the order of the candidate names; an unacceptable `icon.png` does not
   stop `logo.png` being used.
3. A candidate is acceptable when it is not a link, is neither empty nor over `MaxFileBytes`, can be opened by the
   verified open described below, and begins with the 8-byte PNG signature followed by a header chunk whose
   dimensions are both between 1 and `MaxDimension` (24 bytes are read: signature, chunk length and type `IHDR`, then
   width and height). The check is on the *declared* dimensions: a few dozen bytes can declare billions of pixels, and
   refusing them before the decoder allocates anything is what prevents the decoder from running out of memory - the
   unit does not rely on catching `OutOfMemoryException`.

**Decode(file, decodeWidth)**: Reads and decodes a file found by `Find`, scaled to `decodeWidth` pixels wide
(`Bitmap.DecodeToWidth`, high-quality interpolation; the height follows the aspect ratio,
`AgentControl-RepoImageLocator-DecodeToTile`). Everything is validated again, because the repo may have changed
since `Find` looked: the recorded root must still exist and not be a link; the file must still be a candidate name
directly in that root, not a link, within the size cap, with a PNG header within the dimension limits - the same
header check, repeated on the bytes actually read, so a file replaced by an oversized-dimension header is refused
without being decoded (`AgentControl-RepoImageLocator-DimensionLimit`). Needs Avalonia's platform to be initialized
(the decoder is part of it).

**Verified open** (private `OpenVerified`): the replacement-race defense. A path that was checked can be swapped for a
link before it is opened, so the check that counts is made on the *open handle*:

1. Cheap refusals first: the name is a candidate, the file's folder is the root, the file is not a link.
2. The file is opened read-only with `FileShare.Read`.
3. The operating system is then asked which file the open handle is: on Windows `GetFinalPathNameByHandleW`
   (source-generated `LibraryImport` P/Invoke; the `\\?\` prefixes are removed), on Linux the target of
   `/proc/self/fd/{descriptor}`, and where neither exists (macOS, which the product does not ship for) the path is
   resolved link by link as a best effort. The root's own final path is obtained the same way (on Windows by opening
   the folder with backup semantics and asking for its final path; elsewhere by resolving each component).
4. The open file is accepted only if its final path is a file of the same name directly in the root's final path.
   Comparing the *resolved* paths of both means a repo that sits under a linked parent folder still works, while a file
   that resolves to anywhere else - through a link swapped in after the checks - is refused
   (`AgentControl-RepoImageLocator-NoLinks`, `-StaysInRoot`). The comparison is case-insensitive on Windows and
   case-sensitive elsewhere.

A hard link in the root to a file elsewhere is an ordinary file of the root by every test the file system offers
and is not detected; it is a file the user's own account can already read, it is subject to the same size, signature
and dimension checks, and it cannot make the read leave the root.

#### Error Handling

- `Find` treats an unreadable folder or file (`IOException`, `UnauthorizedAccessException`, `ArgumentException`,
  `NotSupportedException`) as "no picture".
- `Decode` returns `null` for any failure, **of any exception type** (`catch (Exception)`): the decoder reports a
  corrupt file with several exception types, nothing from an untrusted file may fault the card's background refresh
  (`AgentControl-RepoImageLocator-NeverFails`), and the caller also guards its own call. This catch is the last
  resort, not the protection against hostile sizes, which is the dimension bound above. File contents are never
  logged.
- Nothing is propagated to the caller.

#### Dependencies

- **Avalonia** (OTS) - `Bitmap.DecodeToWidth`, the PNG decoder.
- The .NET file system APIs, `kernel32` (Windows) and `/proc` (Linux) for the handle's final path.
- No other unit; the project enables unsafe blocks only because the source-generated P/Invoke stubs need it (no
  unsafe code is written by hand).

#### Callers

- **RepoCardViewModel** (`LauncherUI`) - calls `Find` and `Decode` on a worker thread from its background picture
  refresh, remembers the last `RepoImageFile` so an unchanged file is not decoded again, and publishes the bitmap
  on the UI thread.
