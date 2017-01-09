using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AccountServer {
	/// <summary>
	/// This class is stored in a database table
	/// </summary>
	public class TableAttribute : Attribute {
	}

	/// <summary>
	/// This class is be filled from a view
	/// </summary>
	public class ViewAttribute : Attribute {
		public ViewAttribute(string sql) {
			Sql = sql;
		}

		public string Sql;
	}

	/// <summary>
	/// Unique index. Use more than 1 with the same name for compound keys.
	/// </summary>
	public class UniqueAttribute : Attribute {

		public UniqueAttribute(string name)
			: this(name, 0) {
		}

		public UniqueAttribute(string name, int sequence) {
			Name = name;
			Sequence = sequence;
		}

		public string Name { get; private set; }

		public int Sequence { get; private set; }
	}

	/// <summary>
	/// Primary index. Use more than 1 for compound primary keys.
	/// </summary>
	public class PrimaryAttribute : Attribute {

		public PrimaryAttribute()
			: this(0) {
		}

		public PrimaryAttribute(int sequence) {
			Name = "PRIMARY";
			Sequence = sequence;
		}

		public bool AutoIncrement = true;

		public string Name { get; private set; }

		public int Sequence { get; private set; }
	}

	/// <summary>
	/// This field relates to a master record on another table
	/// </summary>
	public class ForeignKeyAttribute : Attribute {
		public ForeignKeyAttribute(string table) {
			Table = table;
		}

		public string Table { get; private set; }
	}

	/// <summary>
	/// Is allowed to be null.
	/// </summary>
	public class NullableAttribute : Attribute {
	}

	/// <summary>
	/// Length - use 0 for Memo string fields, otherwise strings will have length 45.
	/// Decimals are 10.2 by default, doubles 10.4
	/// </summary>
	public class LengthAttribute : Attribute {
		public LengthAttribute(int length) : this(length, 0) {
		}

		public LengthAttribute(int length, int precision) {
			Length = length;
			Precision = precision;
		}

		public int Length;

		public int Precision;
	}

	public class DefaultValueAttribute : Attribute {
		public DefaultValueAttribute(string value) {
			Value = value;
		}

		public DefaultValueAttribute(int value) {
			Value = value.ToString();
		}

		public DefaultValueAttribute(bool value) {
			Value = value ? "1" : "0";
		}

		public string Value;
	}

	/// <summary>
	/// This field is not stored on the database.
	/// </summary>
	public class DoNotStoreAttribute : Attribute {
	}

	/// <summary>
	/// Class to build a data dictionary from the code
	/// </summary>
	public class CodeFirst {
		Dictionary<string, Table> _tables;
		Dictionary<Field, ForeignKeyAttribute> _foreignKeys;

		public CodeFirst() {
			_tables = new Dictionary<string, Table>();
			var baseType = typeof(JsonObject);
			var assembly = baseType.Assembly;
			_foreignKeys = new Dictionary<Field,ForeignKeyAttribute>();
			foreach (Type tbl in assembly.GetTypes().Where(t => t.BaseType == baseType)) {
				if(!tbl.IsDefined(typeof(TableAttribute)))
					continue;
				processTable(tbl, null);
			}
			foreach (Field fld in _foreignKeys.Keys) {
				ForeignKeyAttribute fk = _foreignKeys[fld];
				Table tbl = TableFor(fk.Table);
				fld.ForeignKey = new ForeignKey(tbl, tbl.Fields[0]);
			}
			foreach (Type tbl in assembly.GetTypes().Where(t => t.IsSubclassOf(baseType))) {
				ViewAttribute view = tbl.GetCustomAttribute<ViewAttribute>();
				if (view == null)
					continue;
				processTable(tbl, view);
			}
			_foreignKeys = null;
		}

		public Dictionary<string, Table> Tables {
			get { return new Dictionary<string, Table>(_tables); }
		}
		
		public IEnumerable<string> TableNames {
			get { return _tables.Where(t => !t.Value.IsView).Select(t => t.Key); }
		}

		public IEnumerable<string> ViewNames {
			get { return _tables.Where(t => t.Value.IsView).Select(t => t.Key); }
		}

		public Table TableFor(string name) {
			Table table;
			Utils.Check(_tables.TryGetValue(name, out table), "Table '{0}' does not exist", name);
			return table;
		}

		void processTable(Type tbl, ViewAttribute view) {
			List<Field> fields = new List<Field>();
			// Indexes by name
			Dictionary<string, List<Tuple<int, Field>>> indexes = new Dictionary<string, List<Tuple<int, Field>>>();
			// Primary key fields by sequence
			List<Tuple<int, Field>> primary = new List<Tuple<int, Field>>();
			string primaryName = null;
			processFields(tbl, ref fields, ref indexes, ref primary, ref primaryName);
			if (primary.Count == 0) {
				primary.Add(new Tuple<int, Field>(0, fields[0]));
				primaryName = "PRIMARY";
			}
			List<Index> inds = new List<Index>(indexes.Keys
				.OrderBy(k => k)
				.Select(k => new Index(k, indexes[k]
					.OrderBy(i => i.Item1)
					.Select(i => i.Item2)
					.ToArray())));
			inds.Insert(0, new Index(primaryName, primary
					.OrderBy(i => i.Item1)
					.Select(i => i.Item2)
					.ToArray()));
			if (view != null) {
				Table updateTable = null;
				for(Type t = tbl; updateTable == null && t != typeof(JsonObject); t = t.BaseType)
					_tables.TryGetValue(Regex.Replace(t.Name, "^.*_", ""), out updateTable);
				_tables[tbl.Name] = new View(tbl.Name, fields.ToArray(), inds.ToArray(), view.Sql, updateTable);
			} else {
				_tables[tbl.Name] = new Table(tbl.Name, fields.ToArray(), inds.ToArray());
			}
		}

		void processFields(Type tbl, ref List<Field> fields, ref Dictionary<string, List<Tuple<int, Field>>> indexes, ref List<Tuple<int, Field>> primary, ref string primaryName) {
			if (tbl.BaseType != typeof(JsonObject))	// Process base types first
				processFields(tbl.BaseType, ref fields, ref indexes, ref primary, ref primaryName);
			foreach (FieldInfo field in tbl.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)) {
				if (field.IsDefined(typeof(DoNotStoreAttribute)))
					continue;
				bool nullable = field.IsDefined(typeof(NullableAttribute));
				Type pt = field.FieldType;
				decimal length = 0;
				string defaultValue = null;
				// Convert nullable types to their base type, but set nullable flag
				if (pt == typeof(bool?)) {
					pt = typeof(bool);
					nullable = true;
				} else if (pt == typeof(int?)) {
					pt = typeof(int);
					nullable = true;
				} else if (pt == typeof(decimal?)) {
					pt = typeof(decimal);
					nullable = true;
				} else if (pt == typeof(double?)) {
					pt = typeof(double);
					nullable = true;
				} else if (pt == typeof(DateTime?)) {
					pt = typeof(DateTime);
					nullable = true;
				}
				PrimaryAttribute pk = field.GetCustomAttribute<PrimaryAttribute>();
				if (pk != null)
					nullable = false;			// Primary keys may not be null
				// Set length and default value (may be overridden later by specific attributes)
				if (pt == typeof(bool)) {
					length = 1;
					defaultValue = "0";
				} else if (pt == typeof(int)) {
					length = 11;
					defaultValue = "0";
				} else if (pt == typeof(decimal)) {
					length = 10.2M;
					defaultValue = "0.00";
				} else if (pt == typeof(double)) {
					length = 10.4M;
					defaultValue = "0";
				} else if (pt == typeof(string)) {
					length = 45;
					defaultValue = "";
				}
				if (nullable)
					defaultValue = null;		// If field is nullable, null is always the default value
				LengthAttribute la = field.GetCustomAttribute<LengthAttribute>();
				if (la != null)					// Override length
					length = la.Length + la.Precision / 10M;
				DefaultValueAttribute da = field.GetCustomAttribute<DefaultValueAttribute>();
				if (da != null)					// Override default value
					defaultValue = da.Value;
				Field fld = new Field(field.Name, pt, length, nullable, pk != null && pk.AutoIncrement, defaultValue);
				if (pk != null) {
					primary.Add(new Tuple<int, Field>(pk.Sequence, fld));
					Utils.Check(primaryName == null || primaryName == pk.Name, "2 Primary keys defined on {0}", tbl.Name);
					primaryName = pk.Name;
				}
				// See if the field is in one or more indexes
				foreach (UniqueAttribute a in field.GetCustomAttributes<UniqueAttribute>()) {
					List<Tuple<int, Field>> index;
					if (!indexes.TryGetValue(a.Name, out index)) {
						// New index
						index = new List<Tuple<int, Field>>();
						indexes[a.Name] = index;
					}
					// Add field to index
					index.Add(new Tuple<int, Field>(a.Sequence, fld));
				}
				// See if the field is a foreign key
				ForeignKeyAttribute fk = field.GetCustomAttribute<ForeignKeyAttribute>();
				if (fk != null)
					_foreignKeys[fld] = fk;
				fields.Add(fld);
			}
		}

	}

}
