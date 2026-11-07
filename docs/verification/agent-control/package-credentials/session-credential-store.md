### SessionCredentialStore

#### Verification Approach

`SessionCredentialStore` is verified with unit tests in `SessionCredentialStoreTests.cs` against the real
class with real `FeedOrigin` keys and `NuGetCredential` values; nothing is mocked. Because the guarantee is
that a secret is never written out, one test serializes the store with the JSON serializer - the only way it
could be persisted by accident - and prints it, searching the result for a sentinel secret.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- A credential is found only for the exact origin it was stored under, and a later `Set` replaces it.
- `Forget` removes only the named origin's credential; `Clear` removes all of them.
- Serializing or printing the store never reveals a stored secret.
- Null arguments are refused.

#### Test Scenarios

**SessionCredentialStore_InMemoryOnly**: A stored credential is found for its origin and not for a different
one, a second `Set` replaces the first, null arguments throw, and serializing and printing a store that
holds a sentinel secret yields text without it. This scenario is tested by
`SessionCredentialStore_SetAndTryGet_SameOrigin_ReturnsTheCredential`,
`SessionCredentialStore_TryGet_DifferentOrigin_ReturnsFalse`,
`SessionCredentialStore_Set_ExistingOrigin_ReplacesTheCredential`,
`SessionCredentialStore_Serialize_NeverContainsStoredSecret` and
`SessionCredentialStore_NullArguments_Throw`, covering
`AgentControl-SessionCredentialStore-InMemoryOnly`.

**SessionCredentialStore_ForgetAndClear**: `Forget` removes only the named origin's credential and reports
whether one was removed; `Clear` forgets every credential. This scenario is tested by
`SessionCredentialStore_Forget_StoredOrigin_RemovesOnlyThatCredential` and
`SessionCredentialStore_Clear_StoredCredentials_ForgetsAll`, covering
`AgentControl-SessionCredentialStore-ForgetAndClear`.

**SessionCredentialStore_RegistersSecretsForRedaction**: A stored secret is removed from text by the
redaction engine; replacing the credential withdraws the old secret and registers the new one; `Forget` and
`Clear` withdraw the secret. This scenario is tested by
`SessionCredentialStore_SetReplaceForgetAndClear_RegistersAndWithdrawsTheSecretForRedaction`, covering
`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`.
