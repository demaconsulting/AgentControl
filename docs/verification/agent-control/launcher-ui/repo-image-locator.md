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
linked to requirements as `windows@`; symbolic links need a privilege on Windows, so the symbolic-link tests are
linked as `ubuntu@` (they skip, rather than fail, on an account that may not create one, like the repo's other link
tests). The replacement cases model the race by changing the folder between `Find` and `Decode`; the final-path check
of the open handle (which only matters when a swap happens between the check and the open) cannot be forced from a
test, so it is verified on its positive side - a repo under a linked parent folder, where the resolved paths differ
from the given ones, is still found and decoded - and by review of the design.

#### Test Environment

The headless Avalonia session for `Decode` (no display needed); temporary folders under the system temp folder,
removed after each test without following links; junction creation through `mklink /J` on Windows; symbolic links
through the .NET API where permitted.

#### Acceptance Criteria

- `icon.png` is preferred to `logo.png`; names match in any letter case; an unusable `icon.png` falls through to
  `logo.png`; a missing folder, a folder named `icon.png` and pictures in sub-folders give nothing.
- A file that is empty, that does not start with the PNG signature and header, that is over 1 MB (one of exactly
  1 MB is accepted) or that declares a width or height of zero or over 4096 pixels (4096 x 4096 is accepted) is never
  offered, and a file replaced by an oversized-dimension header after it was offered is refused by `Decode` without
  being decoded.
- A symbolic-link picture, and a picture replaced by a link after it was offered, is never read; a root that is a
  junction or a symbolic link has no picture, also when it was replaced after the picture was offered; a repo under a
  linked parent folder keeps its picture.
- `Decode` refuses a file whose recorded root is not its folder (another folder, a missing folder, a sub-folder) and
  returns no bitmap, without throwing, for a corrupt, vanished or replaced file.
- A valid picture decodes to the tile width keeping its aspect ratio.
- The offered file's identity changes when the file's size or last-write time changes and is equal otherwise.

#### Test Scenarios

**RepoImageLocator_FindsPicture**: Repos with `icon.png` and `logo.png`, only `logo.png`, names in other letter
case, a renamed text file as `icon.png` beside a real `logo.png`, a sub-folder picture with a folder named `logo.png`,
a missing folder and a header declaring the largest allowed size (with a trailing separator on the root) are listed
and the picture offered is checked. This scenario is tested by `RepoImageLocator_Find_IconAndLogoPresent_ReturnsIcon`,
`RepoImageLocator_Find_OnlyLogoPresent_ReturnsLogo`, `RepoImageLocator_Find_NameInOtherCase_IsFound`,
`RepoImageLocator_Find_IconWithoutPngSignature_IsRefusedAndLogoUsed`,
`RepoImageLocator_Find_SubfolderImageAndFolderNamedIcon_AreIgnored`, `RepoImageLocator_Find_MissingFolder_ReturnsNull`
and `RepoImageLocator_Find_LargestAllowedDimensions_IsAcceptedAndTrailingSeparatorIsTolerated`, covering
`AgentControl-RepoImageLocator-FindsPicture`.

**RepoImageLocator_OnlyGenuinePng**: A text file and a file with a JPEG signature named like pictures, and an empty
file, are refused. This scenario is tested by `RepoImageLocator_Find_IconWithoutPngSignature_IsRefusedAndLogoUsed` and
`RepoImageLocator_Find_OnlyNonPngCandidates_ReturnsNull`, covering `AgentControl-RepoImageLocator-OnlyGenuinePng`.

**RepoImageLocator_SizeLimit**: A valid PNG padded to one byte over 1 MB is refused and one of exactly 1 MB is
accepted. This scenario is tested by `RepoImageLocator_Find_FileOverSizeCap_IsRefused`, covering
`AgentControl-RepoImageLocator-SizeLimit`.

**RepoImageLocator_DimensionLimit**: Headers that declare zero, 4097, 8193, 5000 x 5000 and the largest unsigned
values are refused by `Find`; a header declaring 4096 x 4096 is accepted; and after a valid picture was found, the
file is overwritten with a header declaring 4097 x 1, 1 x 4097, 65535 x 65535 or the largest values, which `Decode`
refuses (no bitmap, no exception, nothing decoded). This scenario is tested by
`RepoImageLocator_Find_AbsurdDeclaredDimensions_IsRefused`,
`RepoImageLocator_Find_LargestAllowedDimensions_IsAcceptedAndTrailingSeparatorIsTolerated` and
`RepoImageLocator_Decode_ReplacedByOversizedDimensions_ReturnsNullWithoutDecoding`, covering
`AgentControl-RepoImageLocator-DimensionLimit`.

**RepoImageLocator_NoLinks**: An `icon.png` that is a symbolic link to a valid PNG outside the repo is skipped (the
real `logo.png` beside it wins), and an `icon.png` that is replaced by such a link after it was found is refused by
`Decode`. This scenario is tested by `ubuntu@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused` and
`ubuntu@RepoImageLocator_Decode_IconReplacedBySymbolicLinkAfterFind_ReturnsNull`, covering
`AgentControl-RepoImageLocator-NoLinks`.

**RepoImageLocator_LinkedRoot**: A root that is a junction (Windows) or a symbolic link, leading to a folder with a
valid `icon.png`, has no picture; a root that is replaced by such a link after the picture was found is refused by
`Decode`; and a repo reached through a linked parent folder is found and decoded. This scenario is tested by
`windows@RepoImageLocator_Find_RootIsJunction_ReturnsNull`,
`ubuntu@RepoImageLocator_Find_RootIsSymbolicLink_ReturnsNull`,
`windows@RepoImageLocator_Decode_RootReplacedByJunctionAfterFind_ReturnsNull`,
`ubuntu@RepoImageLocator_Decode_RootReplacedBySymbolicLinkAfterFind_ReturnsNull`,
`windows@RepoImageLocator_FindAndDecode_RootUnderJunctionParent_StillWorks` and
`ubuntu@RepoImageLocator_FindAndDecode_RootUnderSymbolicLinkedParent_StillWorks`, covering
`AgentControl-RepoImageLocator-LinkedRoot`.

**RepoImageLocator_StaysInRoot**: A file found in one repo and claimed for another repo's root, for a missing root and
for a file one folder below the root is refused by `Decode`; a corrupt (valid header, no image data), a deleted and a
replaced (no longer a PNG) file give no bitmap. This scenario is tested by
`RepoImageLocator_Decode_FileOutsideTheRecordedRoot_ReturnsNull` and
`RepoImageLocator_Decode_CorruptVanishedOrReplacedFile_ReturnsNull`, covering
`AgentControl-RepoImageLocator-StaysInRoot`.

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
