class HnswLiteApiError(Exception):
    """Raised when the HnswLite API returns a non-2xx response."""

    def __init__(self, status_code: int, error: str, message: str):
        self.status_code = status_code
        self.error = error
        self.message = message
        super().__init__(f"[{status_code}] {error}: {message}")
