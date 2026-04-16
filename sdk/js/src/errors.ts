/**
 * Error thrown when the HnswLite API returns a non-2xx status code.
 */
export class HnswLiteApiError extends Error {
  public readonly status: number;
  public readonly statusText: string;
  public readonly body: string;

  constructor(status: number, statusText: string, body: string) {
    super(`HnswLite API error ${status} ${statusText}: ${body}`);
    this.name = "HnswLiteApiError";
    this.status = status;
    this.statusText = statusText;
    this.body = body;
  }
}
