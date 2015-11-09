using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Hidden AppModule which allows you to query the database
	/// </summary>
	public class Query : AppModule {
		/// <summary>
		/// Column headings
		/// </summary>
		public JObject Headings;

		/// <summary>
		/// Table name
		/// </summary>
		public string Table;

		/// <summary>
		/// Call with the following parameters:
		/// tables: comma-separated list of tables to join and display
		/// fields: Comma-separated list of fields to display (default all)
		/// f: Field to limit value of
		/// v: Value to limit field to
		/// </summary>
		public override void Default() {
			string tables = GetParameters["tables"];
			Utils.Check(!string.IsNullOrEmpty(tables), "No tables parameter supplied");
			Table = tables.Split(',')[0];
			JObject data = query(tables, "").FirstOrDefault();
			Headings = new JObject();
			foreach (JProperty p in data.Properties()) {
				Headings.Add(p.Name, p.Value.Type == JTokenType.Null ? null : p.Value.Type.ToString().ToLower());
			}
		}

		public IEnumerable<JObject> DefaultListing(string tables) {
			string f = GetParameters["f"];
			string v = GetParameters["v"];
			string where = "";
			if (!string.IsNullOrEmpty(f) && !string.IsNullOrEmpty(v)) {
				Database.CheckValidFieldname(f);
				where = "WHERE " + f + "=" + Database.Quote(v);
			}
			return query(tables, where);
		}

		IEnumerable<JObject> query(string tables, string where) {
			string fields = GetParameters["fields"] ?? "";
			Utils.Check(Regex.IsMatch(fields, @"^[a-z+\*\.,]*$", RegexOptions.IgnoreCase), "Invalid fields parameter {0}", fields);
			return Database.Query(fields, where, tables.Split(','));
		}

	}
}
