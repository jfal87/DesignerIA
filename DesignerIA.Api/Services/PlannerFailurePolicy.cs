using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using DesignerIA.Contracts;
using GitHub.Copilot;

namespace DesignerIA.Api.Services;

internal static class PlannerFailurePolicy
{
    public static ChatResult? Error(string? reason) => reason switch
    {
        "PlannerFailure" => new("error", null, false,
            "No se pudo ejecutar el servicio de IA para interpretar la solicitud. No se aplicaron cambios a la conversación.", ErrorKind: "technical"),
        "PlannerNoResponse" => new("error", null, false,
            "El servicio de IA no devolvió una respuesta para interpretar la solicitud. No se aplicaron cambios a la conversación.", ErrorKind: "technical"),
        "PlannerNoJson" or "InvalidProposalJson" => SemanticError(),
        _ => null
    };

    public static ChatResult Rejected(ConversationPlanValidation validation) => validation.Reason is
        "SubjectAmbiguous" or "ApproximateCandidate" or "SubjectNotFound" or "SubjectReferenceMissing"
        or "ParametersSubjectMissing" or "KnowledgeSubjectMissing" or "ArtifactSubjectMissing"
        or "HandlerSubjectMissing" or "SubjectNotConfirmed"
        ? new("ok", validation.Clarification, false, null)
        : SemanticError();

    private static ChatResult SemanticError() => new("error", null, false,
        "La interpretación recibida del servicio de IA no cumple el contrato de la consulta. No se aplicaron cambios a la conversación.", ErrorKind: "semantic");

    public static void Log(ILogger logger, string requestId, string reason, string phase,
        Exception? exception = null, SessionErrorData? sdkError = null, LogLevel level = LogLevel.Warning)
    {
        var errorType = sdkError?.ErrorType is "authentication" or "authorization" or "quota" or "rate_limit"
            or "context_limit" or "query" ? sdkError.ErrorType : sdkError is null ? null : "Unrecognized";
        var errorCode = sdkError?.ErrorCode is "quota_exceeded" or "session_quota_exceeded" or "billing_not_configured"
            or "user_weekly_rate_limited" or "user_global_rate_limited" or "rate_limited"
            or "user_model_rate_limited" or "integration_rate_limited" ? sdkError.ErrorCode
            : sdkError?.ErrorCode is null ? null : "Unrecognized";
        var category = errorType switch
        {
            "authentication" => "Authentication",
            "authorization" => "Authorization",
            "quota" => "Quota",
            "rate_limit" => "RateLimit",
            "context_limit" => "ContextLimit",
            _ => Classify(exception)
        };
        if (reason == "PlannerNoResponse") { category = "NoResponse"; }
        if (reason is "PlannerNoJson" or "InvalidProposalJson") { category = "InvalidOutput"; }
        var upstreamStatus = sdkError?.StatusCode
            ?? (exception is HttpRequestException { StatusCode: { } status } ? (int?)status : null);
        // Never attach the original exception or raw SDK payload: either can contain prompts or credentials.
        logger.Log(level, "Copilot operation failed. RequestId: {RequestId}, Reason: {Reason}, Phase: {Phase}, FailureCategory: {FailureCategory}, ExceptionType: {ExceptionType}, InnerExceptionType: {InnerExceptionType}, HResult: {HResult}, StackTrace: {StackTrace}, MessageLength: {MessageLength}, MessageFingerprint: {MessageFingerprint}, SdkErrorType: {SdkErrorType}, SdkErrorCode: {SdkErrorCode}, SdkErrorTypeFingerprint: {SdkErrorTypeFingerprint}, SdkErrorCodeFingerprint: {SdkErrorCodeFingerprint}, SdkMessageLength: {SdkMessageLength}, SdkMessageFingerprint: {SdkMessageFingerprint}, UpstreamStatus: {UpstreamStatus}",
            requestId, reason, phase, category, exception?.GetType().FullName, exception?.InnerException?.GetType().FullName,
            exception?.HResult, exception?.StackTrace, exception?.Message.Length, Fingerprint(exception?.Message),
            errorType, errorCode, Fingerprint(sdkError?.ErrorType), Fingerprint(sdkError?.ErrorCode),
            sdkError?.Message.Length, Fingerprint(sdkError?.Message),
            upstreamStatus);
    }

    private static string Classify(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case FileNotFoundException:
                case DirectoryNotFoundException:
                case Win32Exception { NativeErrorCode: 2 or 3 }:
                    return "RuntimeUnavailable";
                case AuthenticationException: return "Authentication";
                case TimeoutException: return "Timeout";
                case OperationCanceledException: return "Cancelled";
                case HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }: return "Authentication";
                case HttpRequestException { StatusCode: HttpStatusCode.Forbidden }: return "Authorization";
                case HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }: return "RateLimit";
                case HttpRequestException:
                case SocketException: return "Transport";
                case IOException { InnerException: null }: return "Transport";
            }
        }
        return "Unclassified";
    }

    private static string? Fingerprint(string? value) => value is null ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}
