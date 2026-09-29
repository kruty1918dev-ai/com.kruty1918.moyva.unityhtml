namespace UnityHTML.Runtime
{
    public readonly struct UnityHtmlMountResult
    {
        private UnityHtmlMountResult(bool succeeded, string errorMessage)
        {
            Succeeded = succeeded;
            ErrorMessage = errorMessage;
        }

        public bool Succeeded { get; }
        public string ErrorMessage { get; }

        public static UnityHtmlMountResult Success() => new UnityHtmlMountResult(true, string.Empty);

        public static UnityHtmlMountResult Failure(string errorMessage)
            => new UnityHtmlMountResult(false, string.IsNullOrWhiteSpace(errorMessage) ? "UnityHTML mount failed." : errorMessage);
    }
}
