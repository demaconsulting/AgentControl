### RepoImageLocator

#### Verification Approach

`RepoImageLocator` is verified with unit tests in `RepoImageLocatorTests.cs` against real temporary folders:
each test creates a repo folder holding small PNG files generated in memory by the test helper `TestPng` (no binary
assets, no imaging library), runs `Find` and `Decode` on it and checks the result. Nothing is mocked - the file system
behavior (links, replacement, handles) *is* the behavior under test. `Decode` needs Avalonia's decoder, so those
tests run on the shared headless Avalonia session (`HeadlessTestApp`, which uses the real Skia drawing backend so
that a picture is genuinely decoded and scaled); the cases that must return no picture before the decoder is reached
call it directly. How the card uses the unit (background loading, not decoding an unchanged file again, falling back
to the tile) is verified in `RepoCardImageTests.cs` as part of the `RepoCardViewModel` unit.

Links need platform filters. Junctions exist only on Windows and need no privilege there, so the junction tests are
linked to requirements as `windows@`. Symbolic links are created through the .NET API: the CI runners of Windows,
Ubuntu and macOS can all create them, so the symbolic-link tests are linked as `windows@`, `ubuntu@` and `macos@` (a
test run on an account that may not create a link skips the test, like the repo's other link tests, and a skipped
test is no evidence). The replacement cases model the race by changing the folder between `Find` and `Decode`.

A swap between the check and the open cannot be forced from a test, so the check of the open handle is verified from
both sides that can be reached. `RepoImageLocator_GetFinalPath_OpenedThroughLinkedFolder_NamesTheRealFile` opens a
file through a symbolic-linked folder and asserts that the operating system's answer to "which file is this handle?"
(`GetFinalPath`, the same call the open uses) is the real path, with the link on the way resolved.
`RepoImageLocator_FindAndDecode_OrdinaryRepo_PassesTheOpenHandleCheck` runs the whole `Find`/`Decode` path on an
ordinary repo on every operating system: because an unknown answer means no picture, a missing or wrong answer
(on macOS, the file-control call with its ARM64 variadic quirk) would make it fail. Those two tests, run on macOS, are the
evidence for the macOS handle check. The positive side of a repo under a linked parent folder, where the resolved
paths differ from the given ones, is verified as well; the rest is by review of the design.

The crowded-root case creates thousands of unrelated files next to a picture spelled in another case; it proves the
picture is still found when the root is looked into by name, not that the cost is flat (no timing assertion is made).

#### Test Environment

The headless Avalonia session for `Decode` (no display needed); temporary folders under the system temp folder,
removed after each test without following links; junction creation through `mklink /J` on Windows; symbolic links
through the .NET API where permitted.

#### Acceptance Criteria

- `icon.png` is preferred to `logo.png`; names match in any letter case; an unusable `icon.png` falls through to
  `logo.png`; a missing folder, a folder named `icon.png` and pictures in sub-folders give nothing; a root holding
  thousands of other files still gives its picture.
- A file that is empty, that does not start with the PNG signature and header, whose header chunk does not declare
  the length 13, that is over 1 MB (one of exactly 1 MB is accepted) or that declares a width or height of zero or
  over 4096 pixels (4096 x 4096 is accepted) is never offered, and a file replaced after it was offered by one with
  an oversized-dimension header or a wrong header chunk length is refused by `Decode` without being decoded.
- A symbolic-link picture, and a picture replaced by a link after it was offered, is never read; a root that is a
  junction or a symbolic link has no picture, also when it was replaced after the picture was offered; a repo under a
  linked parent folder keeps its picture.
- The file behind an open handle is named by the operating system with every link resolved, on Windows, Linux and
  macOS, and the picture of an ordinary repo passes that check on every one of them.
- `Decode` refuses a file whose recorded root is not its folder (another folder, a missing folder, a sub-folder) and
  returns no bitmap, without throwing, for a corrupt, vanished or replaced file.
- A valid picture decodes to the tile width keeping its aspect ratio.
- The offered file's identity changes when the file's size or last-write time changes and is equal otherwise.

#### Test Scenarios

**RepoImageLocator_FindsPicture**: Repos with `icon.png` and `logo.png`, only `logo.png`, and a header declaring the
largest allowed size (with a trailing separator on the root) are listed and the picture offered is checked. This
scenario is tested by `RepoImageLocator_Find_IconAndLogoPresent_ReturnsIcon`,
`RepoImageLocator_Find_OnlyLogoPresent_ReturnsLogo` and
`RepoImageLocator_Find_LargestAllowedDimensions_IsAcceptedAndTrailingSeparatorIsTolerated`, covering
`AgentControl-RepoImageLocator-FindsPicture`.

**RepoImageLocator_LogoFallback**: A text file renamed `icon.png` beside a real `logo.png` is refused and the logo is
offered. This scenario is tested by `RepoImageLocator_Find_IconWithoutPngSignature_IsRefusedAndLogoUsed`, covering
`AgentControl-RepoImageLocator-LogoFallback`.

**RepoImageLocator_CaseInsensitiveNames**: Pictures named `ICON.PNG`, `Icon.png`, `Logo.Png` and `LOGO.PNG` are found.
On a case-sensitive file system (the ubuntu job; the spellings cannot coexist elsewhere, where the tests skip), the
exact `icon.png` is chosen although 40 other spellings exist, so the choice does not follow the file system's listing
order; with no exact spelling the first acceptable spelling in ordinal order of the names is chosen (a refused
`ICON.PNG` gives way to `ICON.png`); and an acceptable spelling beyond the bound of considered spellings is ignored.
This scenario is tested by `RepoImageLocator_Find_NameInOtherCase_IsFound`,
`ubuntu@RepoImageLocator_Find_ManyCaseVariantsAndExactSpelling_PrefersExactSpelling`,
`ubuntu@RepoImageLocator_Find_OnlyCaseVariants_ChoosesFirstAcceptableInOrdinalOrder` and
`ubuntu@RepoImageLocator_Find_AcceptableVariantBeyondTheBound_IsIgnored`, covering
`AgentControl-RepoImageLocator-CaseInsensitiveNames`.

**RepoImageLocator_MissingFolder**: A folder that does not exist gives no picture and no exception. This scenario is
tested by `RepoImageLocator_Find_MissingFolder_ReturnsNull`, covering `AgentControl-RepoImageLocator-MissingFolder`.

**RepoImageLocator_RootOnly**: A valid picture named `icon.png` in a sub-folder and a folder named `logo.png` give
nothing. This scenario is tested by `RepoImageLocator_Find_SubfolderImageAndFolderNamedIcon_AreIgnored`, covering
`AgentControl-RepoImageLocator-RootOnly`.

**RepoImageLocator_CrowdedRoot**: A root holding 3000 unrelated files and a picture whose name has another letter case
still gives that picture. This scenario is tested by `RepoImageLocator_Find_RootWithManyUnrelatedFiles_FindsThePicture`,
covering `AgentControl-RepoImageLocator-CrowdedRoot`.

**RepoImageLocator_OnlyGenuinePng**: A text file and a file with a JPEG signature named like pictures are refused.
This scenario is tested by `RepoImageLocator_Find_IconWithoutPngSignature_IsRefusedAndLogoUsed` and
`RepoImageLocator_Find_OnlyNonPngCandidates_ReturnsNull`, covering `AgentControl-RepoImageLocator-OnlyGenuinePng`.

**RepoImageLocator_NoEmptyFile**: An empty file named like a picture is refused. This scenario is tested by
`RepoImageLocator_Find_OnlyNonPngCandidates_ReturnsNull`, covering `AgentControl-RepoImageLocator-NoEmptyFile`.

**RepoImageLocator_HeaderChunkLength**: A file with a valid signature, chunk type and sane dimensions whose header
chunk declares the length 0, 12, 14, 25 or the largest value is refused by `Find`; and a picture that was found and
then overwritten with a header chunk of length 14 is refused by `Decode` (no bitmap, no exception). This scenario is
tested by `RepoImageLocator_Find_HeaderChunkLengthIsNotThirteen_IsRefused` and
`RepoImageLocator_Decode_ReplacedByWrongHeaderChunkLength_ReturnsNull`, covering
`AgentControl-RepoImageLocator-HeaderChunkLength`.

**RepoImageLocator_SizeLimit**: A valid PNG padded to one byte over 1 MB is refused and one of exactly 1 MB is
accepted. This scenario is tested by `RepoImageLocator_Find_FileOverSizeCap_IsRefused`, covering
`AgentControl-RepoImageLocator-SizeLimit`.

**RepoImageLocator_DimensionLimit**: Headers that declare zero, 4097, 8193, 5000 x 5000 and the largest unsigned
values are refused by `Find`, and a header declaring 4096 x 4096 is accepted. This scenario is tested by
`RepoImageLocator_Find_AbsurdDeclaredDimensions_IsRefused` and
`RepoImageLocator_Find_LargestAllowedDimensions_IsAcceptedAndTrailingSeparatorIsTolerated`, covering
`AgentControl-RepoImageLocator-DimensionLimit`.

**RepoImageLocator_DecodeDimensionLimit**: After a valid picture was found, the file is overwritten with a header
declaring 4097 x 1, 1 x 4097, 65535 x 65535 or the largest values, which `Decode` refuses (no bitmap, no exception,
nothing decoded). This scenario is tested by
`RepoImageLocator_Decode_ReplacedByOversizedDimensions_ReturnsNullWithoutDecoding`, covering
`AgentControl-RepoImageLocator-DecodeDimensionLimit`.

**RepoImageLocator_NoLinks**: An `icon.png` that is a symbolic link to a valid PNG outside the repo is skipped (the
real `logo.png` beside it wins). This scenario is tested by `windows@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused`,
`ubuntu@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused` and `macos@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused`,
covering `AgentControl-RepoImageLocator-NoLinks`.

**RepoImageLocator_LinkSwap**: An `icon.png` that is replaced, after it was found, by a symbolic link to a valid PNG
outside the repo is refused by `Decode`. This scenario is tested by
`windows@RepoImageLocator_Decode_IconReplacedBySymbolicLinkAfterFind_ReturnsNull`,
`ubuntu@RepoImageLocator_Decode_IconReplacedBySymbolicLinkAfterFind_ReturnsNull` and
`macos@RepoImageLocator_Decode_IconReplacedBySymbolicLinkAfterFind_ReturnsNull`, covering
`AgentControl-RepoImageLocator-LinkSwap`.

**RepoImageLocator_LinkedRoot**: A root that is a junction (Windows only) or a symbolic link, leading to a folder with
a valid `icon.png`, has no picture. This scenario is tested by `windows@RepoImageLocator_Find_RootIsJunction_ReturnsNull`
and by `RepoImageLocator_Find_RootIsSymbolicLink_ReturnsNull` on `windows@`, `ubuntu@` and `macos@`, covering
`AgentControl-RepoImageLocator-LinkedRoot`.

**RepoImageLocator_LinkedRootSwap**: A root that is replaced, after the picture was found, by a junction (Windows only)
or a symbolic link to a folder with a valid picture is refused by `Decode`. This scenario is tested by
`windows@RepoImageLocator_Decode_RootReplacedByJunctionAfterFind_ReturnsNull` and by
`RepoImageLocator_Decode_RootReplacedBySymbolicLinkAfterFind_ReturnsNull` on `windows@`, `ubuntu@` and `macos@`,
covering `AgentControl-RepoImageLocator-LinkedRootSwap`.

**RepoImageLocator_LinkedParentKept**: A repo that is an ordinary folder inside a folder reached through a junction
(Windows only) or a symbolic link is found and decoded. This scenario is tested by
`windows@RepoImageLocator_FindAndDecode_RootUnderJunctionParent_StillWorks` and by
`RepoImageLocator_FindAndDecode_RootUnderSymbolicLinkedParent_StillWorks` on `windows@`, `ubuntu@` and `macos@`, and
a parent link whose own target lies below another link (as a temporary folder does on macOS, under `/var`) by
`RepoImageLocator_Find_LinkedParentWhoseTargetIsBelowAnotherLink_StillWorks` on `windows@`, `ubuntu@` and `macos@`,
covering `AgentControl-RepoImageLocator-LinkedParentKept`.

**RepoImageLocator_StaysInRoot**: A file found in one repo and claimed for another repo's root, for a missing root and
for a file one folder below the root is refused by `Decode`. This scenario is tested by
`RepoImageLocator_Decode_FileOutsideTheRecordedRoot_ReturnsNull`, covering `AgentControl-RepoImageLocator-StaysInRoot`.

**RepoImageLocator_ChangedFileRefused**: A corrupt (valid header, no image data), a deleted and a replaced (no longer a
PNG) file give no bitmap. This scenario is tested by `RepoImageLocator_Decode_CorruptVanishedOrReplacedFile_ReturnsNull`,
covering `AgentControl-RepoImageLocator-ChangedFileRefused`.

**RepoImageLocator_OpenFileVerified**: The operating system's final path of a file opened through a symbolic-linked
folder names the real file (not the linked folder), and the picture of an ordinary repo is offered and decoded, which
only happens when the final path of the open file is known and is the named file in the root. This scenario is tested
by `RepoImageLocator_FindAndDecode_OrdinaryRepo_PassesTheOpenHandleCheck` and by
`RepoImageLocator_GetFinalPath_OpenedThroughLinkedFolder_NamesTheRealFile` on `windows@`, `ubuntu@` and `macos@`,
covering `AgentControl-RepoImageLocator-OpenFileVerified`. That no picture is offered where the platform cannot answer
is a property of the design that no test can force on a supporting platform.

**RepoImageLocator_MacOsOpenFileVerified**: The two tests of the previous scenario, run on macOS, prove the
file-control based answer works there: the picture of an ordinary repo is offered and decoded, and the final path of
a file opened
through a symbolic-linked folder is the real path. This scenario is tested by
`macos@RepoImageLocator_FindAndDecode_OrdinaryRepo_PassesTheOpenHandleCheck` and
`macos@RepoImageLocator_GetFinalPath_OpenedThroughLinkedFolder_NamesTheRealFile`, covering
`AgentControl-RepoImageLocator-MacOsOpenFileVerified`.

**RepoImageLocator_DecodeToTile**: A 400 x 200 picture decodes to 96 x 48. This scenario is tested by
`RepoImageLocator_Decode_ValidLargePng_IsScaledDownToTileWidth`, covering
`AgentControl-RepoImageLocator-DecodeToTile`.

**RepoImageLocator_NeverFails**: A corrupt file and a file replaced with an oversized-dimension header give no bitmap
and no exception; the card-level test that makes the decoder throw several exception types, resource exhaustion
included, and checks that the background refresh does not fault, belongs to `RepoCardViewModel`. This scenario is
tested by `RepoImageLocator_Decode_CorruptVanishedOrReplacedFile_ReturnsNull` and
`RepoImageLocator_Decode_ReplacedByOversizedDimensions_ReturnsNullWithoutDecoding`, covering
`AgentControl-RepoImageLocator-NeverFails`.

**RepoImageLocator_ChangeIdentity**: The offered file is equal when nothing changed and differs after it is replaced
by a bigger picture. This scenario is tested by `RepoImageLocator_Find_FileChanged_KeyDiffers`, covering
`AgentControl-RepoImageLocator-ChangeIdentity`.
