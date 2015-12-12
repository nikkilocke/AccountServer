using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.Data;
using System.Data.Common;
using Mono.Data.Sqlite;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Interface to SQLite
	/// </summary>
	public class SQLiteDatabase : DbInterface {
		static object _lock = new object();
		SqliteConnection _conn;
		SqliteTransaction _tran;

		static SQLiteDatabase() {
			SqliteDateDiff.RegisterFunction(typeof(SqliteDateDiff));
			SqliteSum.RegisterFunction(typeof(SqliteSum));
		}

		public SQLiteDatabase(string connectionString) {
			createDatabase(connectionString);
			_conn = new SqliteConnection();
			_conn.ConnectionString = connectionString;
			_conn.Open();
		}

		public void BeginTransaction() {
			lock (_lock) {
				if (_tran == null)
					_tran = _conn.BeginTransaction();
			}
		}

		/// <summary>
		/// Return SQL to cast a value to a type
		/// </summary>
		public string Cast(string value, string type) {
			return value;
		}

		public void CleanDatabase() {
			foreach (string table in Database.TableNames) {
				Table t = Database.TableFor(table);
				if(t.PrimaryKey.AutoIncrement)
					Execute(string.Format("UPDATE sqlite_sequence SET seq = (SELECT MAX({1}) FROM {0}) WHERE name='{0}'",
						table, t.PrimaryKey.Name));
			}
			Execute("VACUUM");
		}

		public void CreateTable(Table t) {
			View v = t as View;
			if (v != null) {
				executeLog(string.Format("CREATE VIEW `{0}` AS {1}", v.Name, v.Sql));
				return;
			}
			createTable(t, t.Name);
			createIndexes(t);
		}

		public void CreateIndex(Table t, Index index) {
			executeLog(string.Format("ALTER TABLE `{0}` ADD CONSTRAINT `{1}` UNIQUE ({2})", t.Name, index.Name,
				string.Join(",", index.Fields.Select(f => "`" + f.Name + "` ASC").ToArray())));
		}

		public void Commit() {
			if (_tran != null) {
				lock (_lock) {
					_tran.Commit();
					_tran.Dispose();
					_tran = null;
				}
			}
		}

		public void Dispose() {
			Rollback();
			if (_conn != null) {
				_conn.Dispose();
				_conn = null;
			}
		}

		public void DropTable(Table t) {
			executeLogSafe("DROP TABLE IF EXISTS " + t.Name);
			executeLogSafe("DROP VIEW IF EXISTS " + t.Name);
		}

		public void DropIndex(Table t, Index index) {
			executeLogSafe(string.Format("ALTER TABLE `{0}` DROP INDEX `{1}`", t.Name, index.Name));
		}

		int Execute(string sql) {
			int lastInserttId;
			return Execute(sql, out lastInserttId);
		}

		public int Execute(string sql, out int lastInsertId) {
			lock (_lock) {
				using (SqliteCommand cmd = command(sql)) {
					var ret = cmd.ExecuteNonQuery();
					cmd.CommandText = "select last_insert_rowid()";
					lastInsertId = (int)(Int64)cmd.ExecuteScalar();
					return ret;
				}
			}
		}

		public bool FieldsMatch(Table t, Field code, Field database) {
			if (code.TypeName != database.TypeName) return false;
			if (t.IsView) return true;	// Database does not always give correct values for view columns
			if (code.AutoIncrement != database.AutoIncrement) return false;
			if (code.Length != database.Length) return false;
			if (code.Nullable != database.Nullable) return false;
			if (code.DefaultValue != database.DefaultValue) return false;
			return true;
		}

		public IEnumerable<JObject> Query(string query) {
			lock (_lock) {
				using (SqliteCommand cmd = command(query)) {
					using (SqliteDataReader r = executeReader(cmd, query)) {
						JObject row;
						while ((row = readRow(r, query)) != null) {
							yield return row;
						}
					}
				}
			}
		}

		public JObject QueryOne(string query) {
			return Query(query + " LIMIT 1").FirstOrDefault();
		}

		static public string Quote(object o) {
			if (o == null || o == DBNull.Value) return "NULL";
			if (o is int || o is long || o is double) return o.ToString();
			if (o is decimal) return ((decimal)o).ToString("0.00");
			if (o is double) return (Math.Round((decimal)o, 4)).ToString();
			if (o is double) return ((decimal)o).ToString("0");
			if (o is bool) return (bool)o ? "1" : "0";
			if (o is DateTime) return "'" + ((DateTime)o).ToString("yyyy-MM-dd") + "'";
			return "'" + o.ToString().Replace("'", "''") + "'";
		}

		public void Rollback() {
			if (_tran != null) {
				lock (_lock) {
					_tran.Rollback();
					_tran.Dispose();
					_tran = null;
				}
			}
		}

		public Dictionary<string, Table> Tables() {
			Dictionary<string, Table> tables = new Dictionary<string, Table>();
			createDatabase(AppSettings.Default.ConnectionString);
			using (SqliteConnection conn = new SqliteConnection(AppSettings.Default.ConnectionString)) {
				conn.Open();
				DataTable tabs = conn.GetSchema("Tables");
				DataTable cols = conn.GetSchema("Columns");
				DataTable fkeyCols = conn.GetSchema("ForeignKeys");
				DataTable indexes = conn.GetSchema("Indexes");
				DataTable indexCols = conn.GetSchema("IndexColumns");
				DataTable views = conn.GetSchema("Views");
				DataTable viewCols = conn.GetSchema("ViewColumns");
				foreach(DataRow table in tabs.Rows) {
					string name = table["TABLE_NAME"].ToString();
					string filter = "TABLE_NAME = " + Quote(name);
					Field[] fields = cols.Select(filter, "ORDINAL_POSITION")
						.Select(c => new Field(c["COLUMN_NAME"].ToString(), typeFor(c["DATA_TYPE"].ToString()), 
							lengthFromColumn(c), c["IS_NULLABLE"].ToString() == "True", c["AUTOINCREMENT"].ToString() == "True", 
							defaultFromColumn(c))).ToArray();
					List<Index> tableIndexes = new List<Index>();
					foreach (DataRow ind in indexes.Select(filter + " AND PRIMARY_KEY = 'True'")) {
						string indexName = ind["INDEX_NAME"].ToString();
						tableIndexes.Add(new Index("PRIMARY", 
							indexCols.Select(filter + " AND INDEX_NAME = " + Quote(indexName), "ORDINAL_POSITION")
							.Select(r => fields.First(f => f.Name == r["COLUMN_NAME"].ToString())).ToArray()));
					}
					foreach (DataRow ind in indexes.Select(filter + " AND PRIMARY_KEY = 'False' AND UNIQUE = 'True'")) {
						string indexName = ind["INDEX_NAME"].ToString();
						tableIndexes.Add(new Index(indexName,
							indexCols.Select(filter + " AND INDEX_NAME = " + Quote(indexName), "ORDINAL_POSITION")
							.Select(r => fields.First(f => f.Name == r["COLUMN_NAME"].ToString())).ToArray()));
					}
					tables[name] = new Table(name, fields, tableIndexes.ToArray());
				}
				foreach (DataRow fk in fkeyCols.Rows) {
					Table detail = tables[fk["TABLE_NAME"].ToString()];
					Table master = tables[fk["FKEY_TO_TABLE"].ToString()];
					Field masterField = master.FieldFor(fk["FKEY_TO_COLUMN"].ToString());
					detail.FieldFor(fk["FKEY_FROM_COLUMN"].ToString()).ForeignKey = new ForeignKey(master, masterField);
				}
				foreach (DataRow table in views.Select()) {
					string name = table["TABLE_NAME"].ToString();
					string filter = "VIEW_NAME = " + Quote(name);
					Field[] fields = viewCols.Select(filter, "ORDINAL_POSITION")
						.Select(c => new Field(c["VIEW_COLUMN_NAME"].ToString(), typeFor(c["DATA_TYPE"].ToString()), 
							lengthFromColumn(c), c["IS_NULLABLE"].ToString() == "True", false,
							defaultFromColumn(c))).ToArray();
					Table updateTable = null;
					tables.TryGetValue(Regex.Replace(name, "^.*_", ""), out updateTable);
					tables[name] = new View(name, fields, new Index[] { new Index("PRIMARY", fields[0]) },
						table["VIEW_DEFINITION"].ToString(), updateTable);
				}
			}
			return tables;
		}

		public void UpgradeTable(Table code, Table database, List<Field> insert, List<Field> update, List<Field> remove,
			List<Field> insertFK, List<Field> dropFK, List<Index> insertIndex, List<Index> dropIndex) {
				for (int i = dropIndex.Count; i-- > 0; ) {
					Index ind = dropIndex[i];
					if ((ind.Fields.Length == 1 && ind.Fields[0].Name == code.PrimaryKey.Name) || ind.Name.StartsWith("sqlite_autoindex_"))
						dropIndex.RemoveAt(i);
				}
			if (update.Count > 0 || remove.Count > 0 || insertFK.Count > 0 || dropFK.Count > 0 || insertIndex.Count > 0 || dropIndex.Count > 0) {
				reCreateTable(code, database);
				return;
			}
			if (insert.Count != 0) {
				foreach(string def in insert.Select(f => "ADD COLUMN " + fieldDef(f))) {
					executeLog(string.Format("ALTER TABLE `{0}` {1}", code.Name, def));
				}
			}
		}

		public bool? ViewsMatch(View code, View database) {
			string c = Regex.Replace(code.Sql, @"[ \r\n\t]+", " ", RegexOptions.Singleline).Trim();
			string d = Regex.Replace(database.Sql, @"[ \r\n\t]+", " ", RegexOptions.Singleline).Trim();
			return c == d;
		}

		SqliteCommand command(string sql) {
			try {
				return new SqliteCommand(sql, _conn, _tran);
			} catch (Exception ex) {
				throw new DatabaseException(ex, sql);
			}
		}

		static void createDatabase(string connectionString) {
			Match m = Regex.Match(connectionString, @"Data Source=([^;]+)", RegexOptions.IgnoreCase);
			if (m.Success && !File.Exists(m.Groups[1].Value)) {
				WebServer.Log("Creating SQLite database {0}", m.Groups[1].Value);
				Directory.CreateDirectory(Path.GetDirectoryName(m.Groups[1].Value));
				SqliteConnection.CreateFile(m.Groups[1].Value);
			}
		}

		void createTable(Table t, string name) {
			List<string> defs = new List<string>(t.Fields.Select(f => fieldDef(f)));
			for (int i = 0; i < t.Indexes.Length; i++) {
				Index index = t.Indexes[i];
				if (i == 0) {
					if (index.Fields.Length != 1 || !index.Fields[0].AutoIncrement)
						defs.Add(string.Format("CONSTRAINT `PRIMARY` PRIMARY KEY ({0})", string.Join(",", index.Fields
							.Select(f => "`" + f.Name + "`").ToArray())));
				} else
					defs.Add(string.Format("CONSTRAINT `{0}` UNIQUE ({1})", index.Name,
						string.Join(",", index.Fields.Select(f => "`" + f.Name + "` ASC").ToArray())));
			}
			defs.AddRange(t.Fields.Where(f => f.ForeignKey != null).Select(f => string.Format(@"CONSTRAINT `fk_{0}_{1}_{2}`
    FOREIGN KEY (`{2}`)
    REFERENCES `{1}` (`{3}`)
    ON DELETE NO ACTION
    ON UPDATE NO ACTION", t.Name, f.ForeignKey.Table.Name, f.Name, f.ForeignKey.Table.PrimaryKey.Name)));
			executeLog(string.Format("CREATE TABLE `{0}` ({1})", name, string.Join(",\r\n", defs.ToArray())));
		}

		void createIndexes(Table t) {
			foreach (string sql in t.Fields.Where(f => f.ForeignKey != null && t.Indexes.FirstOrDefault(i => i.Fields[0] == f) == null)
				.Select(f => string.Format(@"CREATE INDEX `fk_{0}_{1}_{2}_idx` ON {0} (`{2}` ASC)",
				t.Name, f.ForeignKey.Table.Name, f.Name)))
				executeLog(sql);
		}

		static string defaultFromColumn(DataRow def) {
			if (def.IsNull("COLUMN_DEFAULT"))
				return null;
			string r = def["COLUMN_DEFAULT"].ToString();
			Match m = Regex.Match(r, @"^'(.*)'$");
			return m.Success ? m.Groups[1].Value : r;
		}

		int executeLog(string sql) {
			WebServer.Log(sql);
			lock (_lock) {
				using (SqliteCommand cmd = command(sql)) {
					return cmd.ExecuteNonQuery();
				}
			}
		}

		int executeLogSafe(string sql) {
			try {
				return executeLog(sql);
			} catch (Exception ex) {
				WebServer.Log(ex.Message);
				return -1;
			}
		}

		SqliteDataReader executeReader(SqliteCommand cmd, string sql) {
			try {
				return cmd.ExecuteReader();
			} catch (Exception ex) {
				throw new DatabaseException(ex, sql);
			}
		}

		string fieldDef(Field f) {
			StringBuilder b = new StringBuilder();
			b.AppendFormat("`{0}` ", f.Name);
			switch (f.Type.Name) {
				case "Int32":
					b.Append("INTEGER");
					break;
				case "Decimal":
					b.AppendFormat("DECIMAL({0})", f.Length.ToString("0.0").Replace(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, ","));
					break;
				case "Double":
					b.Append("DOUBLE");
					break;
				case "Boolean":
					b.Append("BIT");
					break;
				case "DateTime":
					b.Append("DATETIME");
					break;
				case "String":
					if (f.Length == 0)
						b.Append("TEXT");
					else
						b.AppendFormat("VARCHAR({0})", f.Length);
					b.Append(" COLLATE NOCASE");
					break;
				default:
					throw new CheckException("Unknown type {0}", f.Type.Name);
			}
			if (f.AutoIncrement)
				b.Append(" PRIMARY KEY AUTOINCREMENT");
			else {
				b.AppendFormat(" {0}NULL", f.Nullable ? "" : "NOT ");
				if (f.DefaultValue != null)
					b.AppendFormat(" DEFAULT {0}", Quote(f.DefaultValue));
			}
			return b.ToString();
		}

		decimal lengthFromColumn(DataRow c) {
			try {
				switch (c["DATA_TYPE"].ToString().ToLower()) {
					case "int":
					case "integer":
						return 11;
					case "tinyint":
					case "bit":
						return 1;
					case "decimal":
						string s = c["NUMERIC_PRECISION"] + System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + c["NUMERIC_SCALE"];
						return s == "." ? 10.2M : decimal.Parse(s);
					case "double":
					case "float":
						return 10.4M;
					case "varchar":
						return Convert.ToDecimal(c["CHARACTER_MAXIMUM_LENGTH"]);
					default:
						return 0;
				}
			} catch (Exception ex) {
				WebServer.Log(ex.ToString());
				return 0;
			}
		}

		JObject readRow(SqliteDataReader r, string sql) {
			try {
				lock (_lock) {
					if (!r.Read()) return null;
				}
				JObject row = new JObject();
				for (int i = 0; i < r.FieldCount; i++) {
					row.Add(Regex.Replace(r.GetName(i), @"^.*\.", ""), r[i].ToJToken());
				}
				return row;
			} catch (Exception ex) {
				throw new DatabaseException(ex, sql);
			}
		}

		void reCreateTable(Table code, Table database) {
			string newTable = "_NEW_" + code.Name;
			try {
				executeLogSafe("PRAGMA foreign_keys=OFF");
				executeLog("BEGIN TRANSACTION");
				createTable(code, newTable);
				executeLog(string.Format("INSERT INTO {0} ({2}) SELECT {2} FROM {1}", newTable, database.Name,
					string.Join(", ", code.Fields.Select(f => f.Name)
						.Where(f => database.Fields.FirstOrDefault(d => d.Name == f) != null).ToArray())));
				DropTable(database);
				executeLog("ALTER TABLE " + newTable + " RENAME TO " + code.Name);
				createIndexes(code);
				executeLog("PRAGMA foreign_key_check");
				executeLog("COMMIT TRANSACTION");
			} catch (Exception ex) {
				WebServer.Log("Exception: {0}", ex);
				executeLogSafe("ROLLBACK TRANSACTION");
				throw;
			} finally {
				executeLogSafe("PRAGMA foreign_keys=ON");
			}
		}

		static Type typeFor(string s) {
			switch (s.ToLower()) {
				case "int":
				case "integer":
					return typeof(int);
				case "tinyint":
				case "bit":
					return typeof(bool);
				case "decimal":
					return typeof(decimal);
				case "double":
				case "float":
					return typeof(double);
				case "datetime":
				case "date":
					return typeof(DateTime);
				case "varchar":
				case "text":
				default:
					return typeof(string);
			}
		}

	}

	/// <summary>
	/// DATEDIFF function (like MySql's)
	/// </summary>
	[SqliteFunctionAttribute(Name = "DATEDIFF", Arguments = 2, FuncType = FunctionType.Scalar)]
	class SqliteDateDiff : SqliteFunction {
		public override object Invoke(object[] args) {
			if (args.Length < 2 || args[0] == null || args[0] == DBNull.Value || args[1] == null || args[1] == DBNull.Value)
				return null;
			try {
				DateTime d1 = DateTime.Parse(args[0].ToString());
				DateTime d2 = DateTime.Parse(args[1].ToString());
				return (d1 - d2).TotalDays;
			} catch (Exception ex) {
				WebServer.Log("Exception: {0}", ex);
				return null;
			}
		}
	}

	[SqliteFunctionAttribute(Name = "NOW", Arguments = 0, FuncType = FunctionType.Scalar)]
	class Now : SqliteFunction {
		public override object Invoke(object[] args) {
			try {
				return Utils.Now.ToString("yyyy-MM-ddThh:mm:ss");
			} catch (Exception ex) {
				WebServer.Log("Exception: {0}", ex);
				return null;
			}
		}
	}

	/// <summary>
	/// SUM function which rounds as it sums, so it works like MySql's
	/// </summary>
	[SqliteFunctionAttribute(Name = "SUM", Arguments = 1, FuncType = FunctionType.Aggregate)]
	class SqliteSum : SqliteFunction {
		public override void Step(object[] args, int stepNumber, ref object contextData) {
			if (args.Length < 1 || args[0] == null || args[0] == DBNull.Value)
				return;
			try {
				decimal d = Math.Round(Convert.ToDecimal(args[0]), 4);
				if (contextData != null) d += (Decimal)contextData;
				contextData = d;
			} catch (Exception ex) {
				WebServer.Log("Exception: {0}", ex);
			}
		}

		public override object Final(object contextData) {
			return contextData;
		}
	}
}
