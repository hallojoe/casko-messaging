using System.Collections;
using System.Data.Common;

namespace Casko.Messaging.Email.BulkDelivery;

// Forward-only reader keeps bulk copy memory bounded without constructing tracked entities or DataTables.
internal sealed class BulkRowReader(string[] columns, Type[] types, IEnumerable<object?[]> rows) : DbDataReader
{
    private readonly IEnumerator<object?[]> enumerator = rows.GetEnumerator();
    private bool closed;
    public override int FieldCount => columns.Length;
    public override bool Read() => enumerator.MoveNext();
    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read());
    }
    public override object GetValue(int ordinal) => enumerator.Current[ordinal] ?? DBNull.Value;
    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
    public override string GetName(int ordinal) => columns[ordinal];
    public override int GetOrdinal(string name) => Array.IndexOf(columns, name);
    public override Type GetFieldType(int ordinal) => types[ordinal];
    public override string GetDataTypeName(int ordinal) => types[ordinal].Name;
    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override bool HasRows => true;
    public override bool IsClosed => closed;
    public override int Depth => 0;
    public override int RecordsAffected => -1;
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => new DbEnumerator(this);
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var value = GetString(ordinal);
        if (buffer is null) return value.Length;
        var count = Math.Min(length, Math.Max(0, value.Length - checked((int)dataOffset)));
        value.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }
    public override void Close()
    {
        if (!closed) enumerator.Dispose();
        closed = true;
    }
    protected override void Dispose(bool disposing) { if (disposing) Close(); base.Dispose(disposing); }
}
