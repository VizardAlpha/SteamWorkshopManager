using Steamworks;

namespace SteamWorkshopManager.Services;

/// <summary>
/// Maps Steam API error codes to human-readable messages.
/// </summary>
public static class SteamErrorMapper
{
    /// <summary>
    /// Gets a localized error message for a Steam EResult code.
    /// </summary>
    public static string GetErrorMessage(EResult result)
    {
        var key = GetErrorKey(result);
        return LocalizationService.GetString(key);
    }

    /// <summary>
    /// Gets the localization key for a Steam EResult code.
    /// </summary>
    private static string GetErrorKey(EResult result)
    {
        return result switch
        {
            EResult.k_EResultOK => "SteamOK",
            EResult.k_EResultFail => "SteamFail",
            EResult.k_EResultNoConnection => "SteamNoConnection",
            EResult.k_EResultInvalidPassword => "SteamInvalidPassword",
            EResult.k_EResultLoggedInElsewhere => "SteamLoggedInElsewhere",
            EResult.k_EResultInvalidProtocolVer => "SteamInvalidProtocol",
            EResult.k_EResultInvalidParam => "SteamInvalidParam",
            EResult.k_EResultFileNotFound => "SteamFileNotFound",
            EResult.k_EResultBusy => "SteamBusy",
            EResult.k_EResultInvalidState => "SteamInvalidState",
            EResult.k_EResultInvalidName => "SteamInvalidName",
            EResult.k_EResultInvalidEmail => "SteamInvalidEmail",
            EResult.k_EResultDuplicateName => "SteamDuplicateName",
            EResult.k_EResultAccessDenied => "SteamAccessDenied",
            EResult.k_EResultTimeout => "SteamTimeout",
            EResult.k_EResultBanned => "SteamBanned",
            EResult.k_EResultAccountNotFound => "SteamAccountNotFound",
            EResult.k_EResultInvalidSteamID => "SteamInvalidSteamID",
            EResult.k_EResultServiceUnavailable => "SteamServiceUnavailable",
            EResult.k_EResultNotLoggedOn => "SteamNotLoggedOn",
            EResult.k_EResultPending => "SteamPending",
            EResult.k_EResultEncryptionFailure => "SteamEncryptionFailure",
            EResult.k_EResultInsufficientPrivilege => "SteamInsufficientPrivilege",
            EResult.k_EResultLimitExceeded => "SteamLimitExceeded",
            EResult.k_EResultRevoked => "SteamRevoked",
            EResult.k_EResultExpired => "SteamExpired",
            EResult.k_EResultAlreadyRedeemed => "SteamAlreadyRedeemed",
            EResult.k_EResultDuplicateRequest => "SteamDuplicateRequest",
            EResult.k_EResultAlreadyOwned => "SteamAlreadyOwned",
            EResult.k_EResultIPNotFound => "SteamIPNotFound",
            EResult.k_EResultPersistFailed => "SteamPersistFailed",
            EResult.k_EResultLockingFailed => "SteamLockingFailed",
            EResult.k_EResultLogonSessionReplaced => "SteamLogonSessionReplaced",
            EResult.k_EResultConnectFailed => "SteamConnectFailed",
            EResult.k_EResultHandshakeFailed => "SteamHandshakeFailed",
            EResult.k_EResultIOFailure => "SteamIOFailure",
            EResult.k_EResultRemoteDisconnect => "SteamRemoteDisconnect",
            EResult.k_EResultShoppingCartNotFound => "SteamShoppingCartNotFound",
            EResult.k_EResultBlocked => "SteamBlocked",
            EResult.k_EResultIgnored => "SteamIgnored",
            EResult.k_EResultNoMatch => "SteamNoMatch",
            EResult.k_EResultAccountDisabled => "SteamAccountDisabled",
            EResult.k_EResultServiceReadOnly => "SteamServiceReadOnly",
            EResult.k_EResultAccountNotFeatured => "SteamAccountNotFeatured",
            EResult.k_EResultAdministratorOK => "SteamAdministratorOK",
            EResult.k_EResultContentVersion => "SteamContentVersion",
            EResult.k_EResultTryAnotherCM => "SteamTryAnotherCM",
            EResult.k_EResultPasswordRequiredToKickSession => "SteamPasswordRequiredToKickSession",
            EResult.k_EResultAlreadyLoggedInElsewhere => "SteamAlreadyLoggedInElsewhere",
            EResult.k_EResultSuspended => "SteamSuspended",
            EResult.k_EResultCancelled => "SteamCancelled",
            EResult.k_EResultDataCorruption => "SteamDataCorruption",
            EResult.k_EResultDiskFull => "SteamDiskFull",
            EResult.k_EResultRemoteCallFailed => "SteamRemoteCallFailed",
            EResult.k_EResultExternalAccountUnlinked => "SteamExternalAccountUnlinked",
            EResult.k_EResultPSNTicketInvalid => "SteamPSNTicketInvalid",
            EResult.k_EResultExternalAccountAlreadyLinked => "SteamExternalAccountAlreadyLinked",
            EResult.k_EResultRemoteFileConflict => "SteamRemoteFileConflict",
            EResult.k_EResultIllegalPassword => "SteamIllegalPassword",
            EResult.k_EResultSameAsPreviousValue => "SteamSameAsPreviousValue",
            EResult.k_EResultAccountLogonDenied => "SteamAccountLogonDenied",
            EResult.k_EResultCannotUseOldPassword => "SteamCannotUseOldPassword",
            EResult.k_EResultInvalidLoginAuthCode => "SteamInvalidLoginAuthCode",
            EResult.k_EResultAccountLogonDeniedNoMail => "SteamAccountLogonDeniedNoMail",
            EResult.k_EResultHardwareNotCapableOfIPT => "SteamHardwareNotCapableOfIPT",
            EResult.k_EResultIPTInitError => "SteamIPTInitError",
            EResult.k_EResultParentalControlRestricted => "SteamParentalControlRestricted",
            EResult.k_EResultFacebookQueryError => "SteamFacebookQueryError",
            EResult.k_EResultExpiredLoginAuthCode => "SteamExpiredLoginAuthCode",
            EResult.k_EResultIPLoginRestrictionFailed => "SteamIPLoginRestrictionFailed",
            EResult.k_EResultAccountLockedDown => "SteamAccountLockedDown",
            EResult.k_EResultRateLimitExceeded => "SteamRateLimitExceeded",
            _ => "SteamUnknownError"
        };
    }

    /// <summary>
    /// Gets a detailed technical description for logging purposes.
    /// </summary>
    public static string GetTechnicalDescription(EResult result)
    {
        return $"Steam API Error: {result} (Code: {(int)result})";
    }

    /// <summary>
    /// Checks if the result indicates success.
    /// </summary>
    /// <remarks>
    /// NOTE: Keep this method for future use - do not delete during code cleanup.
    /// Can be used to centralize success checking logic.
    /// </remarks>
    public static bool IsSuccess(EResult result) => result == EResult.k_EResultOK;

    /// <summary>
    /// Checks if the error is recoverable (user can retry).
    /// </summary>
    /// <remarks>
    /// NOTE: Keep this method for future use - do not delete during code cleanup.
    /// Can be used to show a "Retry" button for recoverable errors (timeout, busy, etc.).
    /// </remarks>
    public static bool IsRecoverable(EResult result)
    {
        return result switch
        {
            EResult.k_EResultTimeout => true,
            EResult.k_EResultBusy => true,
            EResult.k_EResultServiceUnavailable => true,
            EResult.k_EResultTryAnotherCM => true,
            EResult.k_EResultRemoteCallFailed => true,
            EResult.k_EResultPending => true,
            EResult.k_EResultNoConnection => true,
            _ => false
        };
    }
}
