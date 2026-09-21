function refresh(wingman) {
  var snapshot = wingman.files.codexTargetSession.turnMetadata();
  if (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot)) {
    return { threadId: null, records: {} };
  }
  var records = snapshot.records;
  return {
    threadId: typeof snapshot.threadId === 'string' ? snapshot.threadId : null,
    records: records && typeof records === 'object' && !Array.isArray(records) ? records : {}
  };
}
