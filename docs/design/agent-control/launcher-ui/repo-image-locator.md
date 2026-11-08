### RepoImageLocator

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoImageLocator` finds and decodes the picture a repo offers for its card: `icon.png`, else `logo.png`, in the
repo's root folder. A repo may be untrusted (the user clones whatever they are asked to work on), so the unit's
single responsibility is to turn "a file some repo put in its folder" into either a small bitmap that is safe to
show or no picture at all, without ever reading outside the repo, exhausting memory or throwing. Its behavior is
specified by the unit requirements `AgentControl-RepoImageLocator-FindsPicture`, `-LogoFallback`,
`-CaseInsensitiveNames`, `-MissingFolder`, `-RootOnly`, `-CrowdedRoot`, `-OnlyGenuinePng`, `-NoEmptyFile`,
`-HeaderChunkLength`, `-SizeLimit`, `-DimensionLimit`, `-DecodeDimensionLimit`, `-NoLinks`, `-LinkSwap`, `-LinkedRoot`,
`-LinkedRootSwap`, `-LinkedParentKept`, `-StaysInRoot`, `-ChangedFileRefused`, `-OpenFileVerified`,
`-MacOsOpenFileVerified`, `-DecodeToTile`, `-NeverFails` and `-ChangeIdentity` (each prefixed
`AgentControl-RepoImageLocator`), and together with `RepoCardViewModel` (which asks for the picture in the background
and exposes it, `AgentControl-RepoCardViewModel-RepoPicture`) it provides `AgentControl-LauncherUI-CardIdentityAndStatus`.

#### Data Model

**RepoImageFile** (`readonly record struct`): `Root`, `Path`, `LastWriteTimeUtc`, `Length` - one validated candidate.
It is also the cache key: the same root, path, last-write time and size means the same picture, so a caller can skip
decoding an unchanged file. `Root` is the repo root `Find` validated; it travels with the file so that `Decode` checks
the file against that root again (`AgentControl-RepoImageLocator-StaysInRoot`). Offering a picture as this record is
what lets a caller tell whether the picture changed (`AgentControl-RepoImageLocator-ChangeIdentity`).

**Limits** (public constants): `MaxFileBytes` = 1 MB (a card icon needs no more, and the cap bounds the memory a
hostile file can use); `MaxDimension` = 4096 pixels (the largest width or height the PNG header may declare, so at
most 4096 x 4096 pixels, 64 MB as a 32-bit bitmap); `TileDecodeWidth` = 96 pixels (twice the 44 pixel tile, so the
picture stays sharp at 200 percent display scaling). Private: `HeaderChunkLength` = 13 (the exact length of the PNG
header chunk, `AgentControl-RepoImageLocator-HeaderChunkLength`) and `MaxCaseVariants` = 8 (the most spellings of one
candidate name, other than the exact one, that are looked at, in ordinal order, see `Find`).

**Candidate names**: `icon.png`, then `logo.png`, matched without regard to letter case.

#### Key Methods

**Find(repoRoot)**: Returns the first acceptable `RepoImageFile`, or `null`.

1. The root must exist and must not itself be a link (a symbolic link, a junction or another reparse point; checked
   by `FileSystemInfo.LinkTarget` and the `ReparsePoint` attribute). **A linked root has no picture**
   (`AgentControl-RepoImageLocator-LinkedRoot`): this is a decision for the picture feature only - other features
   deliberately accept a repo under a linked parent folder, and so does this one; only a root that *is* a link is
   refused, because it lets the repo pick which folder is searched.
2. The folder is never listed. For each candidate name in turn, `EnumerateNamed` asks the file system for the files
   directly in the root whose name matches that name (a streaming enumeration with a literal pattern, no recursion,
   inaccessible entries skipped). The choice is deterministic, never a function of the file system's listing order: the
   exact spelling is probed first (a case-sensitive match on the listed name) and always offered first; the other
   spellings (a seven-letter name has at most 128, so the matches are few) follow in ordinal order of their names, and
   only the first `MaxCaseVariants` of those are offered - a spelling beyond that bound is ignored. A file in a folder
   below the root is never considered. The memory used and
   the number of entries examined do not depend on how many files an untrusted root holds
   (`AgentControl-RepoImageLocator-CrowdedRoot`), where reading the whole listing first would have let a hostile repo
   impose that cost on every refresh. Candidates are examined in the order of the candidate names; an unacceptable
   `icon.png` does not stop `logo.png` being used (`AgentControl-RepoImageLocator-LogoFallback`).
3. A candidate is acceptable when it is not a link, is neither empty nor over `MaxFileBytes`, can be opened by the
   verified open described below, and begins with the 8-byte PNG signature followed by a header chunk whose length is
   exactly `HeaderChunkLength`, whose type is `IHDR`, and whose dimensions are both between 1 and `MaxDimension`
   (24 bytes are read: signature, chunk length and type, then width and height). The length rule rejects malformed
   data that merely starts like a PNG header. The check is on the *declared* dimensions: a few dozen bytes can declare
   billions of pixels, and refusing them before the decoder allocates anything is what prevents the decoder from
   running out of memory - the unit does not rely on catching `OutOfMemoryException`.

**Decode(file, decodeWidth)**: Reads and decodes a file found by `Find`, scaled to `decodeWidth` pixels wide
(`Bitmap.DecodeToWidth`, high-quality interpolation; the height follows the aspect ratio,
`AgentControl-RepoImageLocator-DecodeToTile`). Everything is validated again, because the repo may have changed
since `Find` looked: the recorded root must still exist and not be a link; the file must still be a candidate name
directly in that root, not a link, within the size cap, with a PNG header within the dimension limits - the same
header check (signature, chunk length, dimensions), repeated on the bytes actually read, so a file replaced by an
oversized-dimension header or a header of the wrong chunk length is refused without being decoded
(`AgentControl-RepoImageLocator-DecodeDimensionLimit`, `-HeaderChunkLength`, `-ChangedFileRefused`). Needs
Avalonia's platform to be initialized
(the decoder is part of it).

**Verified open** (private `OpenVerified`): the replacement-race defense. A path that was checked can be swapped for a
link before it is opened, so the check that counts is made on the *open handle*:

1. Cheap refusals first: the name is a candidate, the file's folder is the root, the file is not a link.
2. The root's own final path is determined *before* the file is opened (on Windows by opening the folder with backup
   semantics and asking for its final path; elsewhere by resolving each component), and the root is confirmed again
   not to be a link. Done in this order, a root swapped for a link either is seen as a link, or leads the file open
   into the link's target, which is not the root's recorded final path; resolving the root only after the open would
   let both follow the same swapped link.
3. The file is opened read-only with `FileShare.Read`.
4. The operating system is then asked which file the open handle is (`GetFinalPath`, internal so that tests can ask
   the same question): on Windows `GetFinalPathNameByHandleW` (source-generated `LibraryImport` P/Invoke; the `\\?\`
   prefixes are removed), on Linux the target of `/proc/self/fd/{descriptor}`, and on macOS the file-control function
   of the C library with its get-path command (command number 50), again through a source-generated `LibraryImport`
   P/Invoke. That command makes the kernel write the file's resolved path into a caller-supplied buffer of the
   maximum path length (1024 bytes), which is decoded as UTF-8 up to its terminating zero. The file-control function
   is a variadic C function, and Apple's ARM64 calling convention passes the variadic arguments on the stack, not in
   registers: a declaration with a third parameter would put the buffer in a register where the function does not
   look for it. The ARM64 declaration therefore has nine parameters - the descriptor, the command, six unused filler
   integers that occupy the eight argument registers, and the buffer, which then lands on the stack where the function
   reads its first variadic argument - while x64 uses the plain three-argument declaration; the process architecture
   selects between them. The descriptor is kept alive (reference added) for the duration of the call.
   **There is no fallback**: when the platform is none of these three, or the question fails or is unanswered (a
   non-zero result, an empty answer, a missing library or entry point, a path that is not rooted), the final path is
   unknown, and an unknown final path means *no picture* (`AgentControl-RepoImageLocator-OpenFileVerified`). Resolving
   the path again after the open - which is what an earlier version did where no answer existed - would follow a link
   swapped in between the open and the second resolution to a different file than the one that is open, so it proves
   nothing. That the macOS call really works is evidenced only by test runs on macOS
   (`AgentControl-RepoImageLocator-MacOsOpenFileVerified`).
5. The open file is accepted only if its final path is a file of the same name directly in the root's final path.
   Comparing the *resolved* paths of both means a repo that sits under a linked parent folder still works
   (`AgentControl-RepoImageLocator-LinkedParentKept`), while a file that resolves to anywhere else - through a link
   swapped in after the checks - is refused (`AgentControl-RepoImageLocator-NoLinks`, `-LinkSwap`, `-StaysInRoot`).
   The comparison is case-insensitive on Windows and macOS, whose usual file systems are case-insensitive, and
   case-sensitive elsewhere.

A hard link in the root to a file elsewhere is an ordinary file of the root by every test the file system offers
and is not detected; it is a file the user's own account can already read, it is subject to the same size, signature
and dimension checks, and it cannot make the read leave the root.

**Accepted residual risk - a replaced repository directory.** The root's resolved path is recorded before the file is
opened, the root is confirmed not to be a link, and the open handle's final path is compared with the recorded path;
that closes the replacement of the root by a link at or below it. What remains is a swap of the repository directory
*itself* (or of one of its parents) for a different real directory between those steps. Doing that takes write access
to the repository's parent folder - an attacker who already has it can change the repository outright. Even then the
file must still pass the PNG signature, the `IHDR` chunk with length 13, the 1 MB and 4096 pixel limits and is decoded
down-scaled off the UI thread, so the only effect is which picture appears on that card. Links at or below the root
are refused. This residual risk is accepted and not mitigated further.

#### Error Handling

- `Find` treats an unreadable folder or file (`IOException`, `UnauthorizedAccessException`, `ArgumentException`,
  `NotSupportedException`) as "no picture".
- `Decode` returns `null` for any failure, **of any exception type** (`catch (Exception)`): the decoder reports a
  corrupt file with several exception types, nothing from an untrusted file may fault the card's background refresh
  (`AgentControl-RepoImageLocator-NeverFails`), and the caller also guards its own call. This catch is the last
  resort, not the protection against hostile sizes, which is the dimension bound above. File contents are never
  logged.
- Nothing is propagated to the caller. A platform that cannot name the file behind the open handle is not an error:
  the answer is unknown, and no picture is offered.

#### Dependencies

- **Avalonia** (OTS) - `Bitmap.DecodeToWidth`, the PNG decoder.
- The .NET file system APIs, `kernel32` (Windows), `/proc` (Linux) and the C library's file-control function (macOS)
  for the handle's final path.
- No other unit; the project enables unsafe blocks only because the source-generated P/Invoke stubs need it (no
  unsafe code is written by hand).

#### Callers

- **RepoCardViewModel** (`LauncherUI`) - calls `Find` and `Decode` on a worker thread from its background picture
  refresh, remembers the last `RepoImageFile` so an unchanged file is not decoded again, and publishes the bitmap
  on the UI thread.
