using System.Collections;
using System.Data;
using System.Data.Common;
using Backend.Data;
using Backend.Repositories;
using Moq;

namespace backend.Tests.Repositories;

#pragma warning disable CS8764, CS8765, CS8604
public sealed class FakeDbConnection : DbConnection
{
    private readonly List<ConfiguredResult> _configuredResults = [];
    private ConnectionState _state = ConnectionState.Closed;

    public List<ExecutedCommand> ExecutedCommands { get; } = [];

    public override string? ConnectionString { get; set; } = string.Empty;

    public override string Database => "Fake";

    public override string DataSource => "Fake";

    public override string ServerVersion => "1.0";

    public override ConnectionState State => _state;

    public void WhenCommandContains(
        string fragment,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        _configuredResults.Add(new ConfiguredResult(fragment, rows));
    }

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    public override void Open()
    {
        _state = ConnectionState.Open;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotSupportedException();
    }

    protected override DbCommand CreateDbCommand()
    {
        return new FakeDbCommand(this);
    }

    private IReadOnlyList<Dictionary<string, object?>> GetRows(string commandText)
    {
        var result = _configuredResults.LastOrDefault(item => commandText.Contains(item.Fragment, StringComparison.OrdinalIgnoreCase));

        return result?.Rows ?? [];
    }

    private sealed record ConfiguredResult(
        string Fragment,
        IReadOnlyList<Dictionary<string, object?>> Rows);

    public sealed record ExecutedCommand(
        string CommandText,
        Dictionary<string, object?> Parameters);

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbConnection _connection;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbConnection connection)
        {
            _connection = connection;
        }

        public override string? CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            throw new NotSupportedException();
        }

        public override object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new FakeDbParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            _connection.ExecutedCommands.Add(
                new ExecutedCommand(
                    CommandText,
                    _parameters.Items.ToDictionary(
                        parameter => parameter.ParameterName,
                        parameter => parameter.Value)));

            var table = new DataTable();
            var rows = _connection.GetRows(CommandText);

            if (rows.Count > 0)
            {
                foreach (var columnName in rows[0].Keys)
                {
                    table.Columns.Add(columnName, typeof(object));
                }

                foreach (var row in rows)
                {
                    var values = row.Keys.Select(key => row[key] ?? DBNull.Value).ToArray();
                    table.Rows.Add(values);
                }
            }

            return table.CreateDataReader();
        }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        public List<DbParameter> Items { get; } = [];

        public override int Count => Items.Count;

        public override object SyncRoot => ((ICollection)Items).SyncRoot;

        public override int Add(object value)
        {
            Items.Add((DbParameter)value);
            return Items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear()
        {
            Items.Clear();
        }

        public override bool Contains(object value)
        {
            return Items.Contains((DbParameter)value);
        }

        public override bool Contains(string value)
        {
            return Items.Any(parameter => parameter.ParameterName == value);
        }

        public override void CopyTo(Array array, int index)
        {
            ((ICollection)Items).CopyTo(array, index);
        }

        public override IEnumerator GetEnumerator()
        {
            return Items.GetEnumerator();
        }

        protected override DbParameter GetParameter(int index)
        {
            return Items[index];
        }

        protected override DbParameter GetParameter(string parameterName)
        {
            return Items.Single(parameter => parameter.ParameterName == parameterName);
        }

        public override int IndexOf(object value)
        {
            return Items.IndexOf((DbParameter)value);
        }

        public override int IndexOf(string parameterName)
        {
            return Items.FindIndex(parameter => parameter.ParameterName == parameterName);
        }

        public override void Insert(int index, object value)
        {
            Items.Insert(index, (DbParameter)value);
        }

        public override void Remove(object value)
        {
            Items.Remove((DbParameter)value);
        }

        public override void RemoveAt(int index)
        {
            Items.RemoveAt(index);
        }

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                Items.RemoveAt(index);
            }
        }

        protected override void SetParameter(int index, DbParameter value)
        {
            Items[index] = value;
        }

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                Items[index] = value;
                return;
            }

            Items.Add(value);
        }
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = string.Empty;

        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
#pragma warning restore CS8764, CS8765, CS8604