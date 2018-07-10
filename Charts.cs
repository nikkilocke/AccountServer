using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;
using CodeFirstWebFramework;

namespace AccountServer {
	public class Charts : AppModule {
		/// <summary>
		/// Value fields which can be plotted
		/// </summary>
		List<DecimalField> _fields;
		/// <summary>
		/// Available series break levels
		/// </summary>
		List<ChartField> _series;
		/// <summary>
		/// Filters which can be applied
		/// </summary>
		List<Reports.Filter> _filters;
		/// <summary>
		/// All charts have a date filter
		/// </summary>
		Reports.DateFilter _dates;
		/// <summary>
		/// Settings for plot
		/// </summary>
		[Writeable]
		public class ChartSettings {
			/// <summary>
			/// Y Axis field
			/// </summary>
			public string Y;
			/// <summary>
			/// X axis field
			/// </summary>
			public string X1;
			/// <summary>
			/// X Axis level 2 field
			/// </summary>
			public string X2;
			/// <summary>
			/// CHart type
			/// </summary>
			public string ChartType = "bar";
			/// <summary>
			/// Sort largest value first
			/// </summary>
			public bool SortByValue;
		}
		/// <summary>
		/// Y axis value
		/// </summary>
		public DecimalField _y;
		/// <summary>
		/// X Axis
		/// </summary>
		public ChartField _x1;
		/// <summary>
		/// Second series for X axis
		/// </summary>
		public ChartField _x2;
		/// <summary>
		/// Field description for inclusion in axes
		/// </summary>
		public class ChartField {
			public ChartField(string name) : this(name, name) {
			}
			public ChartField(string name, string fieldName) {
				Name = name;
				Names = new DataNames(fieldName);
			}
			/// <summary>
			/// Field name for display
			/// </summary>
			public string Name;
			/// <summary>
			/// Names for SQL, etc.
			/// </summary>
			public DataNames Names;
			/// <summary>
			/// Stores SQL names
			/// </summary>
			public class DataNames {
				public DataNames(string fieldName) {
					FieldName = fieldName;
					int split = fieldName.ToUpper().IndexOf(" AS ");
					if (split < 0) {
						DataName = SortName = fieldName;
					} else {
						SortName = fieldName.Substring(0, split).Trim();
						DataName = fieldName.Substring(split + 4).Trim();
					}
				}
				/// <summary>
				/// Full name of field for SQL SELECT (e.g. "(Amount + Vat) AS Gross")
				/// </summary>
				public string FieldName;
				/// <summary>
				/// Name of SQL column (e.g. "(Amount + Vat)")
				/// </summary>
				public string SortName;
				/// <summary>
				/// Name of returned column (e.g. "Gross")
				/// </summary>
				public string DataName;
			}
			/// <summary>
			/// Get the names of all the fields needed in the SQL
			/// </summary>
			/// <returns></returns>
			virtual public IEnumerable<DataNames> GetNames() {
				yield return Names;
			}
			/// <summary>
			/// Extract a FieldValue for the field from the record JObject.
			/// A FieldValue may actually contain more than 1 field, for sorting purposes.
			/// </summary>
			virtual public FieldValue ValueFor(JObject record) {
				return new FieldValue<string>(record.AsString(Names.DataName));
			}
			public override string ToString() {
				return Name;
			}
		}
		/// <summary>
		/// Field description for s decimal field (i.e. a value to plot)
		/// </summary>
		public class DecimalField : ChartField {
			public DecimalField(string name) : base(name) {
			}
			public DecimalField(string name, string fieldName) : base(name, fieldName) {
			}
			public override FieldValue ValueFor(JObject o) {
				return new FieldValue<decimal>(o.AsDecimal(Names.DataName));
			}
			/// <summary>
			/// Extract the value directly as a decimal
			/// </summary>
			public decimal Value(JObject record) {
				return record.AsDecimal(Names.DataName);
			}
		}
		/// <summary>
		/// Field description for a date summary field
		/// </summary>
		public class DateField : ChartField {
			/// <summary>
			/// Needed to convert to Year, Quarter
			/// </summary>
			Settings _settings;
			public DateField(Settings settings, string name) : base(name, "DocumentDate") {
				_settings = settings;
			}
			public override FieldValue ValueFor(JObject o) {
				return period(o.AsDate("DocumentDate"));
			}
			public FieldValue ValueFor(DateTime date) {
				return period(date);
			}

			string yearFor(DateTime date) {
				date = _settings.YearStart(date);
				string year = date.ToString("yyyy");
				if (date.Month != 1) {
					date = date.AddYears(1);
					year += "/" + date.ToString("yy");
				}
				return year;
			}

			FieldValue<DateTime, string> period(DateTime date) {
				switch (Name) {
					// case "Day":
					case "Week":
						date = date.AddDays(-(int)date.DayOfWeek).Date;
						return new FieldValue<DateTime, string>(date, date.ToShortDateString());
					case "Month":
						date = new DateTime(date.Year, date.Month, 1);
						return new FieldValue<DateTime, string>(date, date.ToString("MMM-yyyy"));
					case "Quarter":
						DateTime year = _settings.YearStart(date);
						date = _settings.QuarterStart(date);
						int quarter = (int)Math.Round((date - year).TotalDays / 91.25, 0) + 1;
						return new FieldValue<DateTime, string>(date, yearFor(date) + "-Q" + quarter);
					case "Year":
						date = _settings.YearStart(date);
						return new FieldValue<DateTime, string>(date, yearFor(date));
				}
				date = date.Date;
				return new FieldValue<DateTime, string>(date, date.ToShortDateString());
			}
		}
		/// <summary>
		/// Represents a field value
		/// </summary>
		public abstract class FieldValue : IComparable {
			public abstract int CompareTo(object obj);
			public static bool operator ==(FieldValue v1, FieldValue v2) {
				return (object)v1 == null ? (object)v2 == null : v1.Equals(v2);
			}
			public static bool operator !=(FieldValue v1, FieldValue v2) {
				return (object)v1 == null ? (object)v2 != null : !v1.Equals(v2);
			}
			public override bool Equals(object obj) {
				return obj != null && CompareTo(obj) == 0;
			}
			public abstract override int GetHashCode();
		}
		/// <summary>
		/// A field value containing a single field
		/// </summary>
		/// <typeparam name="T">Type of field</typeparam>
		public class FieldValue<T> : FieldValue where T : IComparable {
			public T Value;
			public FieldValue(T v) {
				Value = v;
			}
			public override int CompareTo(object obj) {
				return Value.CompareTo(((FieldValue<T>)obj).Value);
			}

			public override int GetHashCode() {
				return Value.GetHashCode();
			}

			public override string ToString() {
				return Value.ToString();
			}
		}
		/// <summary>
		/// A field value containing 2 fields - the second one is used for display, the first one for sorting.
		/// </summary>
		public class FieldValue<T1, T2> : FieldValue<T2> where T1:IComparable where T2:IComparable {
			public T1 Value1;
			public FieldValue(T1 v1, T2 v2) : base(v2) {
				Value1 = v1;
			}
			public override int CompareTo(object obj) {
				FieldValue<T1,T2> o = (FieldValue<T1, T2>)obj;
				int diff = Value1.CompareTo(o.Value1);
				if (diff == 0)
					diff = Value.CompareTo(o.Value);
				return diff;
			}
		}
		/// <summary>
		/// A field value containing 3 fields - the last one is used for display, the first two for sorting.
		/// </summary>
		public class FieldValue<T1, T2, T3> : FieldValue<T3> where T1 : IComparable where T2 : IComparable where T3 : IComparable {
			public T1 Value1;
			public T2 Value2;
			public FieldValue(T1 v1, T2 v2, T3 v3) : base(v3) {
				Value1 = v1;
				Value2 = v2;
			}
			public override int CompareTo(object obj) {
				FieldValue<T1, T2, T3> o = (FieldValue<T1, T2, T3>)obj;
				int diff = Value1.CompareTo(o.Value1);
				if (diff == 0)
					diff = Value2.CompareTo(o.Value2);
				if (diff == 0)
					diff = Value.CompareTo(o.Value);
				return diff;
			}
		}
		/// <summary>
		/// AccountType must be sorted numerically by id, but the display field is the AcctType string
		/// </summary>
		public class AccountTypeField : ChartField {
			public AccountTypeField() : base("AccountType", "AcctType") {
			}
			DataNames accountType = new DataNames("AccountTypeId");
			public override IEnumerable<DataNames> GetNames() {
				yield return accountType;
				yield return Names;
			}
			public override FieldValue ValueFor(JObject o) {
				return new FieldValue<int, string>(o.AsInt("AccountTypeId"), o.AsString("AcctType"));
			}
		}
		/// <summary>
		/// AccountName must be sorted by AccountTypeId, AccountCode, AccountName
		/// </summary>
		public class AccountNameField : ChartField {
			public AccountNameField() : base("AccountName") {
			}
			DataNames accountType = new DataNames("AccountTypeId");
			DataNames accountCode = new DataNames("AccountCode");
			public override IEnumerable<DataNames> GetNames() {
				yield return accountType;
				yield return accountCode;
				yield return Names;
			}
			public override FieldValue ValueFor(JObject o) {
				return new FieldValue<int, string, string>(o.AsInt("AccountTypeId"), o.AsString("AccountCode"), o.AsString("AccountName"));
			}
		}
		/// <summary>
		/// Names of different types must remain distinct
		/// </summary>
		public class NameField : ChartField {
			public NameField() : base("Name") {
			}
			DataNames nameType = new DataNames("Type");
			public override IEnumerable<DataNames> GetNames() {
				yield return nameType;
				yield return Names;
			}
			public override FieldValue ValueFor(JObject o) {
				return new FieldValue<string, string>(o.AsString("Type"), o.AsString("Name"));
			}
		}
		/// <summary>
		/// List or strings with only 1 of each element allowed - used for field lists
		/// </summary>
		public class NameList : List<string> {
			/// <summary>
			/// Pick out a list of fields from all the provided ChartFields (null ChartFields are ignored)
			/// </summary>
			/// <param name="selector"></param>
			/// <param name="fieldlists"></param>
			public NameList(Func<ChartField.DataNames, string> selector, params ChartField[] fieldlists) {
				foreach(ChartField list in fieldlists) {
					if (list != null)
						AddRange(list.GetNames().Select(n => selector(n)));
				}
			}
			/// <summary>
			/// Concatenate all the lists (deduping as we go)
			/// </summary>
			public NameList(params IEnumerable<string> [] lists) {
				foreach(var names in lists)
					AddRange(names);
			}
			/// <summary>
			/// Add all new names from a list
			/// </summary>
			/// <param name="names"></param>
			public new void AddRange(IEnumerable<string> names) {
				foreach (string name in names)
					Add(name);
			}
			/// <summary>
			/// Add an individual name
			/// </summary>
			/// <param name="name"></param>
			public new void Add(string name) {
				if (!string.IsNullOrEmpty(name) && !this.Contains(name))
					base.Add(name);
			}
			/// <summary>
			/// Return all the names, separated by comma
			/// </summary>
			/// <returns></returns>
			public override string ToString() {
				return string.Join(",", this);
			}
		}

		/// <summary>
		/// The settings for the chart being drawn
		/// </summary>
		ChartSettings _settings = new ChartSettings();

		/// <summary>
		/// List of available charts for Default page
		/// </summary>
		public object ChartList;

		/// <summary>
		/// List available charts
		/// </summary>
		public override void Default() {
			SessionData.Chart = new JObject();
			Dictionary<string, List<JObject>> groups = new Dictionary<string, List<JObject>>();
			groups["Memorised Charts"] = new List<JObject>();
			List<JObject> charts = new List<JObject>();
			addChart(charts, new JObject().AddRange("ReportName", "Income by Account", "ReportType", "IncomeByAccount", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Income by Name", "ReportType", "IncomeByName", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Expenditure by Account", "ReportType", "ExpenditureByAccount", "idReport", 0));
			groups["Specific Charts"] = charts;
			charts = new List<JObject>();
			addChart(charts, new JObject().AddRange("ReportName", "Income", "ReportType", "Income", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Expenditure", "ReportType", "Expenditure", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Assets", "ReportType", "Assets", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Liabilities", "ReportType", "Liabilities", "idReport", 0));
			groups["Standard Charts"] = charts;
			charts = new List<JObject>();
			addChart(charts, new JObject().AddRange("ReportName", "Income & Expenditure", "ReportType", "ProfitAndLoss", "idReport", 0));
			addChart(charts, new JObject().AddRange("ReportName", "Assets & Liabilities", "ReportType", "BalanceSheet", "idReport", 0));
			groups["General Charts"] = charts;
			foreach (JObject report in Database.Query("SELECT idReport, ReportGroup, ReportName, ReportType FROM Report WHERE Chart = 1 ORDER BY ReportGroup, ReportName").ToList()) {
				string group = report.AsString("ReportGroup");
				if (!groups.TryGetValue(group, out charts)) {
					charts = new List<JObject>();
					groups[group] = charts;
				}
				addChart(charts, report);
			}
			ChartList = groups;
		}

		void addChart(List<JObject> charts, JObject chart) {
			if (HasAccess(Info, chart.AsString("ReportType").ToLower(), out int accessLevel))
				charts.Add(chart);
		}

		public void IncomeByAccount(int id) {
			redirect(id, @"{
  ""idReport"": 0,
  ""ReportName"": ""Income by Account"",
  ""ReportType"": ""income"",
  ""version"": 1,
  ""filters"": {
    ""DocumentDate"": {
      ""range"": 11
    }
  },
  ""parameters"": {
    ""Y"": ""Amount"",
    ""X1"": ""AccountName"",
    ""ChartType"": ""pie"",
    ""SortByValue"": true
  }
}");
		}

		public void IncomeByName(int id) {
			redirect(id, @"{
  ""idReport"": 0,
  ""ReportName"": ""Income by Name"",
  ""ReportType"": ""income"",
  ""version"": 1,
  ""filters"": {
    ""DocumentDate"": {
      ""range"": 11
    }
  },
  ""parameters"": {
    ""Y"": ""Amount"",
    ""X1"": ""Name"",
    ""ChartType"": ""pie"",
    ""SortByValue"": true
  }
}");
		}

		void redirect(int id, string json) {
			JObject o = JObject.Parse(json);
			string reportType = o.AsString("ReportType");
			if (id == 0) {
				if (SessionData.Report == null)
					SessionData.Report = new JObject();
				SessionData.Report[reportType] = o;
			}
			Redirect("/charts/" + reportType + "?id=" + id);
		}

		static readonly int[] incomeAcctTypes = new int[] { (int)AcctType.Income, (int)AcctType.OtherIncome };

		public void Income(int id) {
			Record = IncomeSave(getJson(id, "Income Chart"));
			Method = "chart";
		}

		public object IncomeSave(JObject json) {
			return profitAndLossSave(json, "-", incomeAcctTypes);
		}

		public void ExpenditureByAccount(int id) {
			redirect(id, @"{
  ""idReport"": 0,
  ""ReportName"": ""Expenditure by Account"",
  ""ReportType"": ""expenditure"",
  ""version"": 1,
  ""filters"": {
    ""DocumentDate"": {
      ""range"": 11
    }
  },
  ""parameters"": {
    ""Y"": ""Amount"",
    ""X1"": ""AccountName"",
    ""ChartType"": ""pie"",
    ""SortByValue"": true
  }
}");
		}

		static readonly int[] expenditureAcctTypes = new int[] { (int)AcctType.Expense, (int)AcctType.OtherExpense };

		public void Expenditure(int id) {
			Record = ExpenditureSave(getJson(id, "Expenditure Chart"));
			Method = "chart";
		}

		public object ExpenditureSave(JObject json) {
			return profitAndLossSave(json, "", expenditureAcctTypes);
		}

		static readonly int[] profitAndLossAcctTypes = new int[] { (int)AcctType.Income, (int)AcctType.Expense, (int)AcctType.OtherIncome, (int)AcctType.OtherExpense };

		public void ProfitAndLoss(int id) {
			Record = ProfitAndLossSave(getJson(id, "Income & Expenditure Chart"));
			Method = "chart";
		}

		public object ProfitAndLossSave(JObject json) {
			return profitAndLossSave(json, "-", profitAndLossAcctTypes);
		}

		JObjectEnumerable selectAccounts(int[] acctTypes) {
			return Database.Query(@"idAccount AS id, AccountName AS value, AcctType AS category, Protected + HideAccount as hide",
				"WHERE (HideAccount = 0 or HideAccount is null) AND AccountTypeId " + Database.In(acctTypes)
				+ " ORDER BY AccountTypeId, AccountName",
				"Account");
		}

		/// <summary>
		/// Generic save for all P and L charts
		/// </summary>
		/// <param name="json">Report json</param>
		/// <param name="sign">"-" or "" - sign to put in front of all amounts</param>
		/// <param name="acctTypes">List of account types to include or filter from</param>
		/// <returns>Chart data ready for javascript to display</returns>
		object profitAndLossSave(JObject json, string sign, int [] acctTypes) {
			initialiseReport(json);
			setSeries(new AccountTypeField(), new ChartField("AccountCode"), new AccountNameField(), new NameField());
			_fields.Add(new DecimalField("Amount", sign + "Amount AS Amount"));
			_fields.Add(new DecimalField("Vat", sign + "Vat AS Vat"));
			_fields.Add(new DecimalField("Gross", sign + "(Amount + Vat) AS Gross"));
			_settings.Y = "Amount";
			_settings.X1 = "AccountName";
			_filters.Add(_dates = new Reports.DateFilter(Settings, "DocumentDate", Reports.DateRange.LastYear));
			_filters.Add(new Reports.RecordFilter("Account", "Journal.AccountId", selectAccounts(acctTypes)));
			_filters.Add(new Reports.RecordFilter("AccountType", "AccountTypeId", SelectAccountTypes().Where(t => acctTypes.Contains(t.AsInt("id")))));
			_filters.Add(new Reports.RecordFilter("DocumentType", "DocumentTypeId", SelectDocumentTypes()));
			_filters.Add(new Reports.RecordFilter("NameAddress", "NameAddressId", SelectNames()));
			_filters.Add(new Reports.DecimalFilter("JournalAmount", "Amount"));
			readSettings(json);
			string sql = buildSql(acctTypes, out NameList fields, out NameList sort);
			sql += "\r\nORDER BY " + sort;
			return chartJson(json, buildChart(Database.Query(sql)));
		}

		static readonly int[] assetAcctTypes = new int[] {
			(int)AcctType.FixedAsset,
			(int)AcctType.OtherAsset,
			(int)AcctType.AccountsReceivable,
			(int)AcctType.Bank,
			(int)AcctType.Investment,
			(int)AcctType.OtherCurrentAsset
		};

		public void Assets(int id) {
			Record = AssetsSave(getJson(id, "Assets Chart"));
			Method = "chart";
		}

		public object AssetsSave(JObject json) {
			return balancesSave(json, "", assetAcctTypes);
		}

		static readonly int[] liabilityAcctTypes = new int[] {
			(int)AcctType.CreditCard,
			(int)AcctType.AccountsPayable,
			(int)AcctType.OtherCurrentLiability,
			(int)AcctType.LongTermLiability,
			(int)AcctType.OtherLiability
		};

		public void Liabilities(int id) {
			Record = LiabilitiesSave(getJson(id, "Liabilities Chart"));
			Method = "chart";
		}

		public object LiabilitiesSave(JObject json) {
			return balancesSave(json, "", liabilityAcctTypes);
		}

		static readonly int[] balanceSheetAcctTypes = new int[] {
			(int)AcctType.FixedAsset,
			(int)AcctType.OtherAsset,
			(int)AcctType.AccountsReceivable,
			(int)AcctType.Bank,
			(int)AcctType.Investment,
			(int)AcctType.OtherCurrentAsset,
			(int)AcctType.CreditCard,
			(int)AcctType.AccountsPayable,
			(int)AcctType.OtherCurrentLiability,
			(int)AcctType.LongTermLiability,
			(int)AcctType.OtherLiability
		};

		public void BalanceSheet(int id) {
			Record = BalanceSheetSave(getJson(id, "Assets & Liabilities Chart"));
			Method = "chart";
		}

		public object BalanceSheetSave(JObject json) {
			return balancesSave(json, "", balanceSheetAcctTypes);
		}

		/// <summary>
		/// Generic save for all Balance sheet charts
		/// </summary>
		/// <param name="json">Report json</param>
		/// <param name="sign">"-" or "" - sign to put in front of all amounts</param>
		/// <param name="acctTypes">List of account types to include or filter from</param>
		/// <returns>Chart data ready for javascript to display</returns>
		object balancesSave(JObject json, string sign, int[] acctTypes) {
			initialiseReport(json);
			setSeries(new AccountTypeField(), new ChartField("AccountCode"), new AccountNameField());
			_fields.Add(new DecimalField("Amount", sign + "Amount AS Amount"));
			_settings.Y = "Amount";
			_settings.X1 = "AccountName";
			_filters.Add(_dates = new Reports.DateFilter(Settings, "DocumentDate", Reports.DateRange.LastYear));
			_filters.Add(new Reports.RecordFilter("Account", "idAccount", selectAccounts(acctTypes)));
			_filters.Add(new Reports.RecordFilter("AccountType", "AccountTypeId", SelectAccountTypes().Where(t => acctTypes.Contains(t.AsInt("id")))));
			readSettings(json);
			string sql = buildSql(acctTypes, out NameList fields, out NameList sort);
			if (_dates.Active) {
				// We are only covering some dates - need to add opening balances dated 1st day of 1st period
				string periodStart = Database.Quote(_dates.CurrentPeriod()[0]);
				// New field list with fixed DocumentDate
				NameList otherFields = new NameList(fields);
				otherFields[otherFields.IndexOf("DocumentDate")] = Database.Cast(periodStart, "DATETIME") + " AS DocumentDate";
				// Y field will be sum of records to date
				otherFields.AddRange(_y.GetNames().Select(y => "SUM(" + y.SortName + ") AS " + y.DataName));
				// Need to group by other sort fields
				string[] group = sort.Where(s => s != "DocumentDate").ToArray();
				// Final sort will be on output fields of union, instead of input ones
				sort = new NameList(n => n.DataName, _x2, _x1);
				// Exclude date range (range will be all dates up to and excluding period start)
				_dates.Apply = false;
				string sql1 = "SELECT " + otherFields + @"
FROM Journal
LEFT JOIN Account ON Account.idAccount = Journal.AccountId
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN Line ON Line.idLine = Journal.idJournal
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
LEFT JOIN DocumentType ON DocumentType.idDocumentType = Document.DocumentTypeId"
					+ getFilterWhere("AccountTypeId " + Database.In(acctTypes),
						"DocumentDate < " + periodStart);
				_dates.Apply = true;
				if(group.Length > 0)
					sql1 += "\r\nGROUP BY " + string.Join(",", group);
				sql = "SELECT * FROM (\r\nSELECT * FROM (" + sql1 + ") AS ob\r\nWHERE Amount <> 0\r\nUNION\r\n" + sql + ") AS result";
			}
			sql += "\r\nORDER BY " + sort;
			// Set flag to accumulate output figures if in date order
			Chart chart = buildChart(Database.Query(sql));
			//#if false
			Dataset dataset = chart.datasets[0];
			if (_x1.Names.DataName == "DocumentDate") {
				// For balance sheet date order reports, each period's balance accumulates
				foreach (Dataset d in chart.datasets) {
					for (int i = 1; i < d.data.Count; i++) {
						d.data[i] += d.data[i - 1];
					}
				}
				// In date order - calculate any investment values for each date
				DateTime maxDate = _dates.Active ? _dates.CurrentPeriod()[1] : Utils.Today;
				foreach (FieldValue<DateTime, string> period in chart.Labels) {
					DateTime next = nextPeriod(period.Value1);
					if (next > maxDate)
						next = maxDate;
					_dates.Apply = false;
					string sqli = "SELECT AccountTypeId, AccountCode, AccountName, Value AS Amount, "
						+ Database.Quote(period.Value1) + " AS DocumentDate FROM ("
						+ Investments.AccountValue(Database, next) + @") AS AccountValues
JOIN Account ON idAccount = ParentAccountId
" + getFilterWhere();
					_dates.Apply = true;
					foreach (JObject investment in Database.Query(sqli)) {
						FieldValue v;
						if (_x2 != null) {
							v = _x2.ValueFor(investment);
							dataset = chart.datasets.FirstOrDefault(ds => ds.label == v.ToString());
						}
						if (dataset != null) {
							// x1 value
							v = _x1.ValueFor(investment);
							// Add value of investment at period end into data
							int index = chart.Labels.IndexOf(v);
							if (index >= 0)
								dataset.data[index] += _y.Value(investment);
							else
								System.Diagnostics.Debug.WriteLine("X1 value not found:" + v);
						}
					}
				}
			} else {
				_dates.Apply = false;
				string sqli = "SELECT AccountTypeId, AccountCode, AccountName, Value AS Amount FROM ("
					+ Investments.AccountValue(Database, Utils.Today) + @") AS AccountValues
JOIN Account ON idAccount = ParentAccountId
" + getFilterWhere();
				_dates.Apply = true;
				foreach (JObject investment in Database.Query(sqli)) {
					FieldValue v;
					if (_x2 != null) {
						v = _x2.ValueFor(investment);
						dataset = chart.datasets.FirstOrDefault(ds => ds.label == v.ToString());
					}
					if (dataset != null) {
						// x1 value
						v = _x1.ValueFor(investment);
						// Add current investment value into dataset
						dataset.AddValue(v, _y.Value(investment));
					}
				}
			}
			return chartJson(json, chart);
		}

		/// <summary>
		/// Delete memorised chart
		/// </summary>
		public AjaxReturn DeleteChart(int id) {
			Report report = Database.Get<Report>(id);
			Utils.Check(report.ReportGroup == "Memorised Charts", "Chart not found");
			Database.Delete(report);
			return new AjaxReturn() { message = "Chart deleted" };
		}

		/// <summary>
		/// Add/update current chart, with settings, to memorised charts.
		/// </summary>
		/// <param name="json"></param>
		/// <returns></returns>
		public AjaxReturn SaveChart(JObject json) {
			Report report = json.To<Report>();
			report.ReportGroup = "Memorised Charts";
			report.ReportSettings = json.ToString();
			report.Chart = true;
			Database.BeginTransaction();
			Database.Update(report);
			Database.Commit();
			return new AjaxReturn() { message = "Chart saved", id = report.idReport };
		}

		/// <summary>
		/// Set up the series list - always starts with date selectors
		/// </summary>
		/// <param name="series"></param>
		void setSeries(params ChartField [] series) {
			_series.Add(new DateField(Settings, "Day"));
			_series.Add(new DateField(Settings, "Week"));
			_series.Add(new DateField(Settings, "Month"));
			_series.Add(new DateField(Settings, "Quarter"));
			_series.Add(new DateField(Settings, "Year"));
			_series.AddRange(series);
		}

		/// <summary>
		/// Set up any report
		/// </summary>
		/// <param name="json">The posted report parameters</param>
		void initialiseReport(JObject json) {
			string reportType = OriginalMethod.ToLower().Replace("save", "");
			Utils.Check(json.AsString("ReportType").ToLower() == reportType, "Invalid report type");
			dynamic r = SessionData.Report;
			if (r == null)
				SessionData.Report = r = new JObject();
			r[reportType] = json;
			_fields = new List<DecimalField>();
			_series = new List<ChartField>();
			_filters = new List<Reports.Filter>();
		}

		/// <summary>
		/// Read a memorised report (or set up a default report record if id doesn't exist)
		/// </summary>
		/// <param name="type">Default report type</param>
		/// <param name="defaultName">Default report name</param>
		JObject readReport(int id, string type, string defaultName) {
			Report report = Database.Get<Report>(id);
			JObject json;
			if (report.idReport == null) {
				report.ReportType = type;
				report.ReportName = defaultName;
				report.ReportSettings = "{}";
			}
			if (PostParameters != null) {
				JToken j = PostParameters["json"];
				if (j != null)
					report.ReportSettings = j.ToString();
			}
			json = JObject.Parse(report.ReportSettings);
			json["ReportName"] = report.ReportName;
			json["ReportType"] = report.ReportType;
			json["idReport"] = report.idReport;
			Utils.Check(report.ReportType == type, "Invalid report type");
			return json;
		}

		/// <summary>
		/// Get report settings from database or session (or generate default settings)
		/// </summary>
		JObject getJson(int id, string defaultTitle) {
			string reportType = OriginalMethod.ToLower();
			dynamic json = null;
			if ((PostParameters == null || PostParameters["json"] == null) && SessionData.Report != null) {
				json = SessionData.Report[reportType];
			}
			if (json == null || json.idReport != id || json.ReportType.ToString().ToLower() != reportType) {
				json = readReport(id, OriginalMethod, defaultTitle);
			}
			return json;
		}

		/// <summary>
		/// Build standard sql and get field list and sort list
		/// </summary>
		string buildSql(int[] acctTypes, out NameList fields, out NameList sort) {
			sort = new NameList(n => n.SortName, _x2, _x1);
			sort.Add("DocumentDate");
			fields = new NameList(n => n.FieldName, _x1, _x2);
			fields.AddRange(sort);
			NameList allFields = new NameList(fields, _y.GetNames().Select(n => n.FieldName));
			// Build standard sql to use if there are no opening balances to consider
			return "SELECT " + allFields + @"
FROM Journal
LEFT JOIN Account ON Account.idAccount = Journal.AccountId
LEFT JOIN AccountType ON AccountType.idAccountType = Account.AccountTypeId
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN Line ON Line.idLine = Journal.idJournal
LEFT JOIN Document ON Document.idDocument = Journal.DocumentId
LEFT JOIN DocumentType ON DocumentType.idDocumentType = Document.DocumentTypeId"
				+ getFilterWhere("AccountTypeId " + Database.In(acctTypes),
					_dates.Active ? null : "DocumentDate < " + Database.Quote(Utils.Today.AddDays(1)));
		}

		/// <summary>
		/// Get the WHERE clause needed to action the filters
		/// </summary>
		string getFilterWhere(params string[] extraWheres) {
			string[] where = _filters.Where(f => f.Active && f.Apply).Select(f => f.Where(Database)).Concat(extraWheres.Where(w => !string.IsNullOrWhiteSpace(w))).ToArray();
			if (where.Length == 0)
				return "";
			return "\r\nWHERE " + string.Join("\r\nAND ", where);
		}

		/// <summary>
		/// Return the filters as a JObject, for the javascript to display in edit report dialog
		/// </summary>
		JObject getFilters() {
			JObject result = new JObject();
			foreach (Reports.Filter f in _filters) {
				result[f.AsString("data")] = f.Data();
			}
			return result;
		}

		/// <summary>
		/// Read the chart settings from the posted json
		/// </summary>
		void readSettings(JObject json) {
			if (json != null) {
				if (json["filters"] != null) {
					JObject fdata = (JObject)json["filters"];
					foreach (Reports.Filter f in _filters) {
						JToken data = fdata[f.AsString("data")];
						if (data == null)
							continue;
						f.Parse(data);
					}
				}
				JObject sdata = (JObject)json["parameters"];
				if (sdata != null) {
					_settings = sdata.To<ChartSettings>();
				}
			}
			_y = _fields.FirstOrDefault(f => f.Name == _settings.Y) ?? _fields.First();
			_x1 = _series.FirstOrDefault(f => f.Name == _settings.X1) ?? _series.First();
			_x2 = _series.FirstOrDefault(f => f.Name == _settings.X2);
			if (_x2 == _x1)
				_x2 = null;
		}

		/// <summary>
		/// Dataset to pass to Chart.js
		/// </summary>
		public class Dataset {
			public string label;
			/// <summary>
			/// Dictionary of key/total pairs
			/// </summary>
			[JsonIgnore]
			public Dictionary<FieldValue, decimal> Data = new Dictionary<FieldValue, decimal>();
			// We will build this list to pass to Chart.js from the dictionary
			// There will be one element per Chart.label
			public List<decimal> data;
			/// <summary>
			/// We have a key and an amount - accumulate it into the appropriate dictionary entry.
			/// </summary>
			public void AddValue(FieldValue key, decimal value) {
				if (Data.TryGetValue(key, out decimal total))
					value += total;		// Existing value - accumulate
				Data[key] = value;
			}
		}

		/// <summary>
		/// Chart data to pass to Chart.js
		/// </summary>
		public class Chart {
			[JsonIgnore]
			public List<FieldValue> Labels;
			public List<string> labels;
			public List<Dataset> datasets = new List<Dataset>();
		}

		/// <summary>
		/// Find the date of the next period after the argument
		/// </summary>
		DateTime nextPeriod(DateTime date) {
			switch (_x1.Name) {
				case "Day":
					return date.AddDays(1);
				case "Week":
					date = date.AddDays(-(int)date.DayOfWeek);
					return date.AddDays(7);
				case "Month":
					return date.AddMonths(1);
				case "Quarter":
					date = Settings.QuarterStart(date);
					return Settings.QuarterStart(date.AddMonths(4));
				case "Year":
					return date.AddYears(1);
			}
			return DateTime.MaxValue;
		}

		/// <summary>
		/// Build Chart object from data
		/// </summary>
		Chart buildChart(IEnumerable<JObject> data) {
			Chart chart = new Chart();
			Dataset dataset = null;
			bool dateOrder = _x1.Names.DataName == "DocumentDate";
			FieldValue x2 = null;
			DateTime minDate = _dates.Active ? _dates.CurrentPeriod()[0] : DateTime.MaxValue;
			DateTime maxDate = _dates.Active ? _dates.CurrentPeriod()[1] : DateTime.MinValue;
			if (_x2 == null) {
				// Only 1 dataset - create it now
				dataset = new Dataset() { label = "Total" };
				chart.datasets.Add(dataset);
			}
			foreach (JObject record in data) {
				if (dateOrder) { 
					// Record min and max dates
					DateTime date = record.AsDate("DocumentDate").Date;
					if (date < minDate)
						minDate = date;
					if (date > maxDate)
						maxDate = date;
				}
				FieldValue v;
				if(_x2 != null) {
					// Multiple datasets - do we need a new one yet
					v = _x2.ValueFor(record);
					if (v != x2) {
						// Yes - create it
						dataset = new Dataset() { label = v.ToString() };
						chart.datasets.Add(dataset);
						x2 = v;
					}
				}
				// x1 value
				v = _x1.ValueFor(record);
				// Accumulate figure for current record into dataset
				dataset.AddValue(v, _y.Value(record));
			}
			// All values to display in chart
			List<FieldValue> labels;
			if (dateOrder) {
				// We require labels and values for every period between min and max dates
				DateField d = (DateField)_x1;
				Dictionary<int, decimal> investmentValues = new Dictionary<int, decimal>();
				chart.Labels = new List<FieldValue>();
				for (DateTime date = minDate; date <= maxDate; date = nextPeriod(date))
					chart.Labels.Add(d.ValueFor(date));
			} else {
				if (_settings.SortByValue) {
					// We require the labels sorted by the value in the first dataset
					chart.Labels = chart.datasets[0].Data.OrderByDescending(i => i.Value).Select(i => i.Key).ToList();
				} else {
					// The labels should be the union of all the distinct labels in the datasets, sorted appropriately
					chart.Labels = chart.datasets.SelectMany(d => d.Data.Keys).OrderBy(k => k).Distinct().ToList();
				}
			}
			// Build the data lists for each dataset
			foreach (Dataset d in chart.datasets) {
				// 1 value for each label
				d.data = chart.Labels.Select(l => d.Data.TryGetValue(l, out decimal val) ? val : 0).ToList();
			}
			// Label strings for Chart.js
			chart.labels = new List<string>(chart.Labels.Select(l => l.ToString()));
			return chart;
		}

		/// <summary>
		/// Produce chart output from original request and chart data
		/// </summary>
		/// <param name="json">Original request</param>
		/// <param name="chart">Chart data</param>
		/// <returns>Json to send to javascript</returns>
		public JObject chartJson(JObject json, Chart chart) {
			// Use ReportName as right hand end of page title
			Title = Regex.Replace(Title, "-[^-]*$", "- " + json.AsString("ReportName"));
			json["version"] = 1;
			json["fields"] = _fields.Select(f => new { value = f.Name }).ToJToken();
			json["x1Options"] = _series.Select(f => new { value = f.Name }).ToJToken();
			List<ChartField> x2Fields = new List<ChartField>();
			x2Fields.Add(new ChartField(""));
			x2Fields.AddRange(_series.Where(f => f.Names.FieldName != "DocumentDate"));
			json["x2Options"] = x2Fields.Select(f => new { value = f.Name }).ToJToken();
			json["filters"] = getFilters().ToJToken();
			json["parameters"] = _settings.ToJToken();
			JObject result = new JObject().AddRange(
				"filters", new JArray(_filters),
				"readonly", ReadOnly,
				"chart", chart.ToJToken(),
				"settings", json,
				"dateorder", _x1.Names.DataName == "DocumentDate"
				);
			return result;
		}


	}
}
