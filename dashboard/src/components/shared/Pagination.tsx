interface PaginationProps {
  skip: number;
  maxResults: number;
  totalRecords: number;
  onPageChange: (skip: number) => void;
  onPageSizeChange: (maxResults: number) => void;
  pageSizeOptions?: number[];
}

/**
 * Offset-based pagination control. Assumes the server enforces a ceiling on
 * pageSize (HnswLite caps maxResults at 1000).
 */
export default function Pagination(props: PaginationProps) {
  const {
    skip,
    maxResults,
    totalRecords,
    onPageChange,
    onPageSizeChange,
    pageSizeOptions = [10, 25, 50, 100, 250, 500, 1000],
  } = props;

  const currentPage = Math.floor(skip / maxResults) + 1;
  const totalPages = Math.max(1, Math.ceil(totalRecords / maxResults));
  const fromRecord = totalRecords === 0 ? 0 : skip + 1;
  const toRecord = Math.min(skip + maxResults, totalRecords);

  function goto(page: number): void {
    const clamped = Math.min(Math.max(1, page), totalPages);
    onPageChange((clamped - 1) * maxResults);
  }

  return (
    <div className="pagination">
      <div className="pagination-info">
        {totalRecords === 0
          ? 'No records'
          : `${fromRecord.toLocaleString()}–${toRecord.toLocaleString()} of ${totalRecords.toLocaleString()}`}
      </div>

      <div className="pagination-controls">
        <div className="pagination-size">
          <label htmlFor="page-size-select">Page size</label>
          <select
            id="page-size-select"
            value={maxResults}
            onChange={(e) => onPageSizeChange(parseInt(e.target.value, 10))}
          >
            {pageSizeOptions.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </div>

        <div className="pagination-nav">
          <button
            type="button"
            className="pagination-btn"
            onClick={() => goto(1)}
            disabled={currentPage <= 1}
            title="First"
            aria-label="First page"
          >
            «
          </button>
          <button
            type="button"
            className="pagination-btn"
            onClick={() => goto(currentPage - 1)}
            disabled={currentPage <= 1}
            title="Previous"
            aria-label="Previous page"
          >
            ‹
          </button>
          <span className="pagination-current">
            Page {currentPage.toLocaleString()} of {totalPages.toLocaleString()}
          </span>
          <button
            type="button"
            className="pagination-btn"
            onClick={() => goto(currentPage + 1)}
            disabled={currentPage >= totalPages}
            title="Next"
            aria-label="Next page"
          >
            ›
          </button>
          <button
            type="button"
            className="pagination-btn"
            onClick={() => goto(totalPages)}
            disabled={currentPage >= totalPages}
            title="Last"
            aria-label="Last page"
          >
            »
          </button>
        </div>
      </div>
    </div>
  );
}
