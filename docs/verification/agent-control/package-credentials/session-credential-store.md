### SessionCredentialStore

#### Verification Approach

`SessionCredentialStore` is verified with unit tests in `SessionCredentialStoreTests.cs` against the real
class with real `FeedOrigin` keys and `NuGetCredential` values; nothing is mocked. Because the guarantee is
that a secret is never written out, one test serializes the store with the JSON serializer - the only way it
could be persisted by accident - and prints it, searching the result for a sentinel secret. The look-up race test
holds a look-up delegate in flight, clears the store and then lets the delegate call `Set`, so the late store is
exercised deterministically; the fault test uses a delegate that throws.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- Serializing or printing the store never reveals a stored secret.
- A credential is found only for the exact origin it was stored under, and a later `Set` replaces it.
- Null arguments are refused.
- `Forget` removes only the named origin's credential; `Clear` removes all of them.
- Forgetting an exact rejected credential leaves a different one that was stored for the same origin meanwhile.
- The record of whether a credential came from the user's credential store is reported, replaced when a different
  credential is stored, and dropped when the credential is forgotten.
- The look-up of the user's credential store runs once per origin per session however many callers ask, and afresh
  after the store is cleared.
- A faulting look-up is surfaced to every caller waiting for it and is not repeated.
- A look-up still in flight when the store is cleared cannot store or register a credential afterwards.
- Every stored secret is registered for redaction while held and withdrawn when replaced, forgotten or cleared; a
  caller's lease keeps it redacted until disposed.

#### Test Scenarios

**SessionCredentialStore_InMemoryOnly**: Serializing and printing a store that holds a sentinel secret yields text
without it. This scenario is tested by `SessionCredentialStore_Serialize_NeverContainsStoredSecret`, covering
`AgentControl-SessionCredentialStore-InMemoryOnly`.

**SessionCredentialStore_KeyedByOrigin**: A stored credential is found for its origin and not for a different one.
This scenario is tested by `SessionCredentialStore_SetAndTryGet_SameOrigin_ReturnsTheCredential` and
`SessionCredentialStore_TryGet_DifferentOrigin_ReturnsFalse`, covering
`AgentControl-SessionCredentialStore-KeyedByOrigin`.

**SessionCredentialStore_ReplaceOnSet**: A second `Set` for an origin replaces the first and the count stays one. This
scenario is tested by `SessionCredentialStore_Set_ExistingOrigin_ReplacesTheCredential`, covering
`AgentControl-SessionCredentialStore-ReplaceOnSet`.

**SessionCredentialStore_NullArgumentsRefused**: A null origin or credential throws `ArgumentNullException`. This
scenario is tested by `SessionCredentialStore_NullArguments_Throw`, covering
`AgentControl-SessionCredentialStore-NullArgumentsRefused`.

**SessionCredentialStore_ForgetAndClear**: `Forget` removes only the named origin's credential and reports
whether one was removed. This scenario is tested by
`SessionCredentialStore_Forget_StoredOrigin_RemovesOnlyThatCredential`, covering
`AgentControl-SessionCredentialStore-ForgetAndClear`.

**SessionCredentialStore_ClearForgetsAll**: `Clear` forgets every credential. This scenario is tested by
`SessionCredentialStore_Clear_StoredCredentials_ForgetsAll`, covering
`AgentControl-SessionCredentialStore-ClearForgetsAll`.

**SessionCredentialStore_ForgetExactCredential**: Forgetting a rejected credential removes the origin's entry only
when it is still that exact credential; a different credential stored for the origin meanwhile is kept and the call
reports that nothing was removed. This scenario is tested by
`SessionCredentialStore_ForgetExactCredential_OnlyRemovesTheSameCredential`, covering
`AgentControl-SessionCredentialStore-ForgetExactCredential`.

**SessionCredentialStore_RememberedOrigin**: A credential stored as coming from the user's credential store is
reported as such, also by the longer `TryAcquire`; storing a different credential without the mark clears it (the
replaced record); forgetting the credential drops it (the dropped record). All three scenarios are tested by
`SessionCredentialStore_FromRememberedStoreMark_FollowsTheCredential`, covering
`AgentControl-SessionCredentialStore-RememberedOrigin`,
`AgentControl-SessionCredentialStore-RememberedOriginReplaced` and
`AgentControl-SessionCredentialStore-RememberedOriginDropped`.

**SessionCredentialStore_RememberedLookupOnce**: Several simultaneous and later requests for the same origin run the
look-up delegate once and all wait for the same task; another origin runs its own look-up. This scenario is tested
by `SessionCredentialStore_EnsureRememberedLookup_RunsOncePerOrigin`, covering
`AgentControl-SessionCredentialStore-RememberedLookupOnce`.

**SessionCredentialStore_RememberedLookupAfterClear**: After the store is cleared the look-up is allowed again, and
a look-up that starts after the clear stores what it finds normally. This scenario is tested by
`SessionCredentialStore_EnsureRememberedLookup_RunsOncePerOrigin` and
`SessionCredentialStore_EnsureRememberedLookup_LookupAfterClear_StartsFreshAndStoresNormally`, covering
`AgentControl-SessionCredentialStore-RememberedLookupAfterClear`.

**SessionCredentialStore_RememberedLookupFault**: A look-up that faults surfaces the fault to every caller waiting
for it, and a later caller gets the same fault without the delegate running again. This scenario is tested by
`SessionCredentialStore_EnsureRememberedLookup_LookupFaults_SurfacesToEveryCallerAndIsNotRepeated`, covering
`AgentControl-SessionCredentialStore-RememberedLookupFaultSurfaced` and
`AgentControl-SessionCredentialStore-RememberedLookupFaultNotRepeated`.

**SessionCredentialStore_ClearInvalidatesInFlightLookups**: A look-up is held in flight, the store is cleared, and
the look-up is then released so that it calls `Set`; the credential is neither stored nor found, and its secret is
not registered with the redaction engine. This scenario is tested by
`SessionCredentialStore_EnsureRememberedLookup_ClearWhileLookupInFlight_LateSetIsDiscardedAndNotRedacted`, covering
`AgentControl-SessionCredentialStore-ClearInvalidatesInFlightLookups` and
`AgentControl-SessionCredentialStore-DiscardedLookupSecretNotRegistered`.

**SessionCredentialStore_RegistersSecretsForRedaction**: A stored secret is removed from text by the
redaction engine; replacing the credential withdraws the old secret and registers the new one; `Forget` and
`Clear` withdraw the store's registration. This scenario is tested by
`SessionCredentialStore_SetReplaceForgetAndClear_RegistersAndWithdrawsTheSecretForRedaction`, covering
`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction` and
`AgentControl-SessionCredentialStore-WithdrawsSecretsForRedaction`.

**SessionCredentialStore_AcquireGivesRedactionLease**: A lease taken with `TryAcquire` keeps the secret redacted
after the store has forgotten it, until it is disposed (twice is harmless); a missing credential yields no lease.
This scenario is tested by
`SessionCredentialStore_TryAcquire_LeaseOutlivesTheStoresOwnRegistrationUntilDisposed`, covering
`AgentControl-SessionCredentialStore-AcquireGivesRedactionLease`.
