using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Web;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// For Scheduled transactions
	/// </summary>
	enum RepeatType {
		None, Daily, Weekly, Monthly, Quarterly, Yearly
	}

	/// <summary>
	/// Company front page, and todo list (including scheduled transactions)
	/// </summary>
	public class Home : AppModule {

		protected override void Init() {
			base.Init();
			insertMenuOptions(
				new MenuOption("Summary", "/home/default.html"),
				new MenuOption("To Do", "/home/schedule.html")
				);
			if (!SecurityOn || UserAccessLevel >= AccessLevel.ReadWrite)
				insertMenuOptions(
					new MenuOption("New To Do", "/home/job.html?id=0")
				);
		}

		public override void Default() {
			Record = new JObject().AddRange(
				"schedule", DefaultScheduleListing(),
				"banking", total(Database.Query("Account.*, AcctType, SUM(Amount) AS Balance", 
					"WHERE AccountTypeId " + Database.In(AcctType.Bank, AcctType.CreditCard) 
					+ " AND DocumentDate <= " + Database.Quote(Utils.Today)
					+ " AND HideAccount != 1 GROUP BY idAccount ORDER BY AccountTypeId, AccountName",
					"Account", "Journal", "Document"), "AcctType", "Balance"),
				"investments", total(Database.Query(@"SELECT Account.*, Amount AS CashBalance, Value
FROM (SELECT AccountId, SUM(Amount) AS Amount FROM Journal GROUP BY AccountId) AS Balances
JOIN Account ON idAccount = Balances.AccountId
JOIN AccountType ON idAccountType = AccountTypeId
LEFT JOIN (" + Investments.AccountValue(Database, Utils.Today) + @") AS AccountValues ON AccountValues.ParentAccountId = Balances.AccountId
WHERE AccountTypeId = " + (int)AcctType.Investment + @"
AND (Amount <> 0 OR Value <> 0)
GROUP BY idAccount ORDER BY AccountName"), "Name", "CashBalance", "Value"),
				"customer", total(Database.Query(@"SELECT NameAddress.*, Sum(Outstanding) AS Outstanding
FROM NameAddress
LEFT JOIN Journal ON NameAddressId = idNameAddress
AND AccountId = " + (int)Acct.SalesLedger + @"
WHERE Type='C'
AND Outstanding <> 0
GROUP BY idNameAddress
ORDER BY Name
"), "Name", "Outstanding"),
				"supplier", total(Database.Query(@"SELECT NameAddress.*, Sum(Outstanding) AS Outstanding
FROM NameAddress
LEFT JOIN Journal ON NameAddressId = idNameAddress
AND AccountId = " + (int)Acct.PurchaseLedger + @"
WHERE Type='S'
AND Outstanding <> 0
GROUP BY idNameAddress
ORDER BY Name
"), "Name", "Outstanding")
				);
		}

		public object DefaultScheduleListing() {
			return Database.Query("SELECT idSchedule, ActionDate, RepeatType, RepeatFrequency, Task, Post, CASE WHEN ActionDate <= " + Database.Quote(Utils.Today) + " THEN 'due' ELSE NULL END AS \"@class\" FROM Schedule WHERE ActionDate <= "
					+ Database.Quote(Utils.Today.AddDays(7)) + " ORDER BY ActionDate");
		}

		public decimal NetWorth;

		public void Schedule() {
		}

		public object ScheduleListing() {
			return Database.Query("SELECT idSchedule, ActionDate, RepeatType, RepeatFrequency, Task, Post FROM Schedule ORDER BY ActionDate");
		}

		/// <summary>
		/// Get a todo job for editing
		/// </summary>
		public void Job(int id) {
			Schedule job = Database.Get<Schedule>(id);
			if (job.idSchedule == null) {
				job.ActionDate = Utils.Today;
			}
			Record = job;
		}

		public AjaxReturn JobSave(Schedule json) {
			Utils.Check(json.RepeatFrequency > 0, "Repeat frequency must be > 0");
			return SaveRecord(json, false);
		}

		public AjaxReturn JobDelete(int id) {
			AjaxReturn result = new AjaxReturn();
			try {
				Database.Delete("Schedule", id);
				result.message = "Job deleted";
			} catch {
				result.error = "Cannot delete";
			}
			return result;
		}

		/// <summary>
		/// Action a job
		/// </summary>
		public AjaxReturn JobAction(int id) {
			AjaxReturn ret = new AjaxReturn();
			Schedule job = Database.Get<Schedule>(id);
			Utils.Check(job.idSchedule != null, "Job {0} not found", id);
			if (!string.IsNullOrWhiteSpace(job.Url)) {
				// Job actually does something
				if (job.Post) {
					// It posts a record
					string methodName = job.Url;
					string moduleName = Utils.NextToken(ref methodName, "/");
					ModuleInfo info = Server.NamespaceDef.GetModuleInfo(moduleName);
					Utils.Check(info != null, "Invalid schedule job {0}", job.Url);
					AppModule module = (AppModule)Activator.CreateInstance(info.Type);
					module.CopyFrom = this;
					module.OriginalModule = module.Module = moduleName.ToLower();
					module.OriginalMethod = module.Method = (string.IsNullOrEmpty(methodName) ? "default" : Path.GetFileNameWithoutExtension(methodName)).ToLower();
					module.GetParameters = new NameValueCollection();
					module.Parameters["json"] = job.Parameters;
					module.Parameters["date"] = job.ActionDate;
					MethodInfo method;
					object o = module.CallMethod(out method);
					if (method == null) {
						ret.error = "Job url not found " + job.Url;
					} else if (method.ReturnType == typeof(AjaxReturn)) {
						ret = o as AjaxReturn;
						if (ret.error == null && ret.redirect != null)
							ret.redirect += "&from=" + HttpUtility.UrlEncode(Parameters.AsString("from")) + "&postjob=1";
						ret.id = null;
					} else {
						throw new CheckException("Unexpected return type {0}", method.ReturnType.Name);
					}
				} else {
					// It just redirects somewhere
					ret.redirect = Path.ChangeExtension(job.Url, ".html") + "?id=" + id;
				}
			}
			if (string.IsNullOrEmpty(ret.error)) {
				// Update job to say it is done
				switch ((RepeatType)job.RepeatType) {
					case RepeatType.None:
						// No repeat - delete job
						Database.Delete(job);
						ret.message = "Job deleted";
						return ret;
					case RepeatType.Daily:
						job.ActionDate = job.ActionDate.AddDays(job.RepeatFrequency);
						while (job.ActionDate.DayOfWeek == DayOfWeek.Saturday || job.ActionDate.DayOfWeek == DayOfWeek.Sunday)
							job.ActionDate = job.ActionDate.AddDays(1);
						break;
					case RepeatType.Weekly:
						job.ActionDate = job.ActionDate.AddDays(7 * job.RepeatFrequency);
						break;
					case RepeatType.Monthly:
						job.ActionDate = job.ActionDate.AddMonths(job.RepeatFrequency);
						break;
					case RepeatType.Quarterly:
						job.ActionDate = job.ActionDate.AddMonths(3 * job.RepeatFrequency);
						break;
					case RepeatType.Yearly:
						job.ActionDate = job.ActionDate.AddYears(job.RepeatFrequency);
						break;
					default:
						throw new CheckException("Invalid repeat type {0}", job.RepeatType);
				}
				Database.Update(job);
			}
			ret.id = job.idSchedule;
			return ret;
		}

		/// <summary>
		/// Select all items with a value in one of the field names.
		/// If there was at least one, add a total row at the bottom, with each fieldname set to its total,
		/// and totalFieldName set to the total of all them.
		/// Also add the grand total into NetWorth.
		/// </summary>
		IEnumerable<JObject> total(IEnumerable<JObject> list, string totalFieldName, params string[] fieldnames) {
			bool addTotal = false;
			decimal[] totals = new decimal[fieldnames.Length];
			foreach (JObject j in list) {
				bool include = false;
				for (int i = 0; i < fieldnames.Length; i++) {
					decimal d = j.AsDecimal(fieldnames[i]);
					totals[i] += d;
					if (d != 0)
						include = true;
				}
				if (include) {
					yield return j;
					addTotal = true;
				}
			}
			JObject tot = new JObject();
			tot["@class"] = "total";
			tot[totalFieldName] = "Total";
			for (int i = 0; i < fieldnames.Length; i++) {
				tot[fieldnames[i]] = totals[i];
				NetWorth += totals[i];
			}
			if(addTotal)
				yield return tot;
		}

	}
}
