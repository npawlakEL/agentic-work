const ConflictDialog = ({ isOpen, message, onRefresh }) => {
  if (!isOpen) {
    return null;
  }

  return (
    <div className="dialog-backdrop" role="presentation">
      <div
        aria-labelledby="conflict-dialog-title"
        aria-modal="true"
        className="dialog"
        role="dialog"
      >
        <h2 id="conflict-dialog-title">Concurrent Edit Detected</h2>
        <p>{message}</p>
        <button className="button button-primary" onClick={onRefresh} type="button">
          Refresh
        </button>
      </div>
    </div>
  );
};

export default ConflictDialog;
