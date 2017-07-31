using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// Banking module has some functionality in common with Banking (e.g. NameAddress maintenance)
	/// </summary>
	public class Banking : BankingAccounting {

		protected override void Init() {
			insertMenuOptions(
				new MenuOption("Listing", "/banking/default.html"),
				new MenuOption("Names", "/banking/names.html")
				);
			if (!SecurityOn || UserAccessLevel >= AccessLevel.ReadWrite)
				insertMenuOptions(
					new MenuOption("New Account", "/banking/detail.html?id=0")
				);
		}

		/// <summary>
		/// List all bank accounts and credit cards
		/// </summary>
		/// <returns></returns>
		public object DefaultListing() {
			return Database.Query("Account.*, AcctType, SUM(Amount) AS Balance",
				"WHERE AccountTypeId " + Database.In(AcctType.Bank, AcctType.CreditCard) + " GROUP BY idAccount ORDER BY AccountName",
				"Account", "Journal");
		}

		/// <summary>
		/// Retrieve a bank/credit card account for editing
		/// </summary>
		public void Detail(int id) {
			BankingDetail record = Database.QueryOne<BankingDetail>("Account.*, AcctType, SUM(Amount) AS Balance",
				"WHERE idAccount = " + id,
				"Account", "Journal");
			// Subtract future transactions to get current balance
			record.CurrentBalance = record.Balance - Database.QueryOne("SELECT SUM(Amount) AS Future FROM Journal JOIN Document ON idDocument = DocumentId WHERE AccountId = "
				+ id + " AND DocumentDate > " + Database.Quote(Utils.Today)).AsDecimal("Future");
			if (record.Id != null) {
				checkAcctType(record.AccountTypeId, AcctType.Bank, AcctType.CreditCard);
				Title += " - " + record.AccountName;
			}
			Record = record;
		}

		/// <summary>
		/// Journal listing for an account
		/// </summary>
		public IEnumerable<JObject> DetailListing(int id) {
			return detailsWithBalance(id).Reverse();
		}

		/// <summary>
		/// Update account details after editing
		/// </summary>
		public AjaxReturn DetailSave(Account json) {
			checkAcctType(json.AccountTypeId, AcctType.Bank, AcctType.CreditCard);
			return SaveRecord(json, true);
		}

		/// <summary>
		/// Get a specific document (or a filled in new document) for this account
		/// </summary>
		internal JObject GetDocument(int id, DocType type) {
			Extended_Document header = getDocument<Extended_Document>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)type;
				header.DocType = type.UnCamel();
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				if (GetParameters["acct"].IsInteger()) {
					FullAccount acct = Database.QueryOne<FullAccount>("*", "WHERE idAccount = " + GetParameters["acct"], "Account");
					if (acct.idAccount != null) {
						header.DocumentAccountId = (int)acct.idAccount;
						header.DocumentAccountName = acct.AccountName;
					}
				}
			} else {
				checkDocType(header.DocumentTypeId, DocType.Cheque, DocType.Deposit, DocType.CreditCardCharge, DocType.CreditCardCredit);
			}
			return new JObject().AddRange("header", header,
				"detail", Database.Query("idJournal, DocumentId, Line.VatCodeId, VatRate, JournalNum, Journal.AccountId, Memo, LineAmount, VatAmount",
					"WHERE Journal.DocumentId = " + id + " AND idLine IS NOT NULL ORDER BY JournalNum",
					"Document", "Journal", "Line"));
		}

		/// <summary>
		/// Get a document for editing
		/// </summary>
		public void Document(int id, DocType type) {
			Title = Title.Replace("Document", type.UnCamel());
			JObject record = GetDocument(id, type);
			dynamic header = ((dynamic)record).header;
			nextPreviousDocument(record, "JOIN Journal ON DocumentId = idDocument WHERE DocumentTypeId = " + (int)type
				+ (header.DocumentAccountId > 0 ? " AND AccountId = " + header.DocumentAccountId : ""));
			record.AddRange("Accounts", SelectAccounts(),
				"VatCodes", SelectVatCodes(),
				"Names", SelectOthers());
			Record = record;
		}

		public AjaxReturn DocumentDelete(int id) {
			return deleteDocument(id, DocType.Cheque, DocType.Deposit, DocType.CreditCardCharge, DocType.CreditCardCredit);
		}

		/// <summary>
		/// Update a document after editing
		/// </summary>
		public AjaxReturn DocumentSave(BankingDocument json) {
			Database.BeginTransaction();
			Extended_Document document = json.header;
			JObject oldDoc = getCompleteDocument(document.idDocument);
			DocType t = checkDocType(document.DocumentTypeId, DocType.Cheque, DocType.Deposit, DocType.CreditCardCharge, DocType.CreditCardCredit);
			FullAccount acct = Database.Get<FullAccount>((int)document.DocumentAccountId);
			checkAcctType(acct.AccountTypeId, AcctType.Bank, AcctType.CreditCard, AcctType.Investment);
			allocateDocumentIdentifier(document, acct);
			int sign = SignFor(t);
			Extended_Document original = getDocument(document);
			decimal vat = 0;
			decimal net = 0;
			bool lineVat = false;		// Flag to indicate this is a cheque to pay the VAT to HMRC
			foreach (InvoiceLine detail in json.detail) {
				net += detail.LineAmount;
				vat += detail.VatAmount;
			}
			Utils.Check(document.DocumentAmount == net + vat, "Document does not balance");
			decimal changeInDocumentAmount = -sign * (document.DocumentAmount - original.DocumentAmount);
			int lineNum = 1;
			fixNameAddress(document, "O");
			Database.Update(document);
			int nextDocid = Utils.ExtractNumber(document.DocumentIdentifier);
			if (nextDocid > 0 && acct.RegisterNumber(t, nextDocid))
				Database.Update(acct);
			// Find any existing VAT record
			Journal vatJournal = Database.QueryOne<Journal>("SELECT * FROM Journal WHERE DocumentId = " + document.idDocument
				+ " AND AccountId = " + (int)Acct.VATControl + " ORDER BY JournalNum DESC");
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)document.idDocument,
				JournalNum = lineNum
			});
			journal.DocumentId = (int)document.idDocument;
			journal.AccountId = document.DocumentAccountId;
			journal.NameAddressId = document.DocumentNameAddressId;
			journal.Memo = document.DocumentMemo;
			journal.JournalNum = lineNum++;
			journal.Amount += changeInDocumentAmount;
			journal.Outstanding += changeInDocumentAmount;
			Database.Update(journal);
			foreach (InvoiceLine detail in json.detail) {
				if (detail.AccountId == 0 || detail.AccountId == null) continue;
				Utils.Check(!lineVat, "Cheque to VAT account may only have 1 line");
				if (detail.AccountId == (int)Acct.VATControl) {
					// This is a VAT payment to HMRC
					Utils.Check(lineNum == 2, "Cheque to VAT account may only have 1 line");
					Utils.Check(vat == 0, "Cheque to VAT account may not have a VAT amount");
					vat = detail.LineAmount;
					lineVat = true;
				}
				journal = Database.Get(new Journal() {
					DocumentId = (int)document.idDocument,
					JournalNum = lineNum
				});
				journal.DocumentId = (int)document.idDocument;
				journal.JournalNum = lineNum++;
				journal.AccountId = (int)detail.AccountId;
				journal.NameAddressId = document.DocumentNameAddressId;
				journal.Memo = detail.Memo;
				journal.Amount = sign * detail.LineAmount;
				journal.Outstanding = sign * detail.LineAmount;
				Database.Update(journal);
				Line line = new Line() {
					idLine = journal.idJournal,
					Qty = 0,
					LineAmount = detail.LineAmount,
					VatCodeId = detail.VatCodeId,
					VatRate = detail.VatRate,
					VatAmount = detail.VatAmount
				};
				Database.Update(line);
			}
			Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum + ")");
			Database.Execute("DELETE FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum);
			if (vat != 0 || vatJournal.idJournal != null) {
				// Add the VAT journal at the end
				vat *= sign;
				decimal changeInVatAmount = vat - vatJournal.Amount;
				Utils.Check(document.VatPaid == null || document.VatPaid < 1 || changeInVatAmount == 0, "Cannot alter VAT on this document, it has already been declared");
				if (!lineVat) {
					vatJournal.DocumentId = (int)document.idDocument;
					vatJournal.AccountId = (int)Acct.VATControl;
					vatJournal.NameAddressId = document.DocumentNameAddressId;
					vatJournal.Memo = "Total VAT";
					vatJournal.JournalNum = lineNum++;
					vatJournal.Amount = vat;
					vatJournal.Outstanding += changeInVatAmount;
					Database.Update(vatJournal);
				}
			}
			JObject newDoc = getCompleteDocument(document.idDocument);
			Database.AuditUpdate("Document", document.idDocument, oldDoc, newDoc);
			Settings.RegisterNumber(this, document.DocumentTypeId, Utils.ExtractNumber(document.DocumentIdentifier));
			Database.Commit();
			return new AjaxReturn() { message = "Document saved", id = document.idDocument };
		}

		/// <summary>
		/// Bank reconciliation
		/// </summary>
		/// <param name="id"></param>
		[Auth(AccessLevel.ReadWrite, Hide = true)]
		public void Reconcile(int id) {
			JObject header = Database.QueryOne("*", "WHERE idAccount = " + id, "Account");
			JObject openingBalance = Database.QueryOne("SELECT SUM(Amount) AS OpeningBalance FROM Journal WHERE AccountId = " + id 
				+ " AND Cleared = 'X'");
			header["OpeningBalance"] = openingBalance == null ? 0 : openingBalance.AsDecimal("OpeningBalance");
			Title += " - " + header.AsString("AccountName");
			checkAccountIsAcctType(id, AcctType.Bank, AcctType.CreditCard);
			Record = new JObject().AddRange("header", header,
				"detail", Database.Query(@"SELECT Extended_Document.*, Journal.idJournal, Journal.Cleared, Journal.Amount
FROM Journal
JOIN Extended_Document ON idDocument = DocumentId
WHERE Journal.AccountId = " + id + @"
AND Journal.Cleared <> 'X'
ORDER BY DocumentDate, idDocument"));
		}

		/// <summary>
		/// Save bank reconciliation
		/// </summary>
		public AjaxReturn ReconcileSave(ReconcileDocument json) {
			// Temporary indicates they haven't finished - no need to check balances, save Clr marks as "*" instead of "X"
			string mark = json.Temporary ? "*" : "X";
			decimal bal = json.header.OpeningBalance;
			Utils.Check(json.header.idAccount > 0, "Invalid account");
			Database.BeginTransaction();
			if (!json.Temporary)
				Database.Audit(AuditType.Reconcile, "Reconciliation", json.header.idAccount, json.ToJson(), null);
			Database.Execute("UPDATE Account SET EndingBalance = " + Database.Quote(json.Temporary ? json.header.EndingBalance : null)
				+ " WHERE idAccount = " + json.header.idAccount);
			foreach (ReconcileLine line in json.detail) {
				string mk;
				if (line.Cleared == "1" || line.Cleared == "*") {
					bal += line.Amount;
					mk = mark;
				} else {
					mk = "";
				}
				Database.Execute("UPDATE Journal SET Cleared = " + Database.Quote(mk) + " WHERE idJournal = " + line.idJournal);
			}
			Utils.Check(json.Temporary || json.header.EndingBalance != null, "You must enter an Ending Balance");
			Utils.Check(json.Temporary || bal == json.header.EndingBalance && bal == json.header.ClearedBalance, "Reconcile does not balance");
			Database.Commit();
			return new AjaxReturn() { message = "Reconcile saved", redirect = json.print ? null : "/Banking/Detail?id=" + json.header.idAccount };
		}

		/// <summary>
		/// Prepare to memorise a transaction for automatic retrieval and saving later.
		/// </summary>
		public void Memorise(int id) {
			dynamic record = GetDocument(id, DocType.Cheque);
			Utils.Check(record.header.idDocument != null, "Document {0} not found", id);
			DocType type = (DocType)record.header.DocumentTypeId;
			Schedule job = new Schedule() {
				ActionDate = record.header.DocumentDate,
				Task = type.UnCamel() + " " + record.header.DocumentAmount.ToString("0.00") + (type == DocType.Cheque || type == DocType.CreditCardCharge ? " to " : " from ") + record.header.DocumentName + " " + record.header.DocumentMemo,
				Url = "banking/standingordersave",
				Parameters = record.ToString(),
				RepeatFrequency = 1,
				Post = true
			};
			Module = "home";
			Method = "job";
			Record = job;
		}

		/// <summary>
		/// Save a memorised transaction schedule record after editing/review
		/// </summary>
		public AjaxReturn MemoriseSave(Schedule json) {
			return SaveRecord(json, false);
		}

		/// <summary>
		/// Save a memorised transaction, then redirect to it for review
		/// </summary>
		public AjaxReturn StandingOrderSave(BankingDocument json, DateTime date) {
			json.header.idDocument = null;
			json.header.DocumentDate = date;
			if (Utils.ExtractNumber(json.header.DocumentIdentifier) > 0)
				json.header.DocumentIdentifier = "";
			AjaxReturn result = DocumentSave(json);
			if (result.error == null && result.id > 0)
				result.redirect = "/banking/document.html?message=" + json.header.DocType.UnCamel() + "+saved&id=" + result.id + "&type=" + json.header.DocumentTypeId;
			return result;
		}

		/// <summary>
		/// Prepare to memorise a transaction for automatic retrieval and saving later.
		/// </summary>
		public void MemoriseTransfer(int id) {
			TransferDocument header = GetTransferDocument(id);
			Utils.Check(header.idDocument != null, "Transfer {0} not found", id);
			Account account = Database.Get<Account>((int)header.TransferAccountId);
			checkDocType(header.DocumentTypeId, DocType.Transfer);
			Schedule job = new Schedule() {
				ActionDate = header.DocumentDate,
				Task = "Transfer " + header.DocumentAmount.ToString("0.00") + " from " + header.DocumentAccountName + " to " + account.AccountName + " " + header.DocumentMemo,
				Url = "banking/repeattransfersave",
				Parameters = header.ToString(),
				RepeatFrequency = 1,
				Post = true
			};
			Module = "home";
			Method = "job";
			Record = job;
		}

		/// <summary>
		/// Save a memorised transaction schedule record after editing/review
		/// </summary>
		public AjaxReturn MemoriseTransferSave(Schedule json) {
			return SaveRecord(json, false);
		}

		/// <summary>
		/// Save a memorised transaction, then redirect to it for review
		/// </summary>
		public AjaxReturn RepeatTransferSave(TransferDocument json, DateTime date) {
			json.idDocument = null;
			json.DocumentDate = date;
			AjaxReturn result = TransferSave(json);
			if (result.error == null && result.id > 0)
				result.redirect = "/banking/transfer.html?message=Transfer+saved&id=" + result.id;
			return result;
		}

		/// <summary>
		/// Show ImportHelp template
		/// </summary>
		public void ImportHelp() {
		}

		/// <summary>
		/// Statement import form
		/// </summary>
		[Auth(AccessLevel.ReadWrite, Hide = true)]
		public void StatementImport(int id) {
			Account account = Database.Get<Account>(id);
			checkAcctType(account.AccountTypeId, AcctType.Bank, AcctType.CreditCard);
			Title += " - " + account.AccountName;
			Record = new JObject().AddRange(
				"Id", id,
				"StatementFormat", account.StatementFormat);
			SessionData.Remove("StatementImport");
			SessionData.Remove("StatementMatch");
		}

		/// <summary>
		/// User wants to import a statement
		/// </summary>
		/// <param name="id">Account</param>
		/// <param name="format">Statement format (for pasted statement)</param>
		/// <param name="data">Pasted statement</param>
		/// <param name="file">Uploaded Qif statement</param>
		/// <param name="dateFormat">For Qif import</param>
		public void StatementImportSave(int id, string format, string data, UploadedFile file, string dateFormat) {
			Account account = Database.Get<Account>(id);
			checkAcctType(account.AccountTypeId, AcctType.Bank, AcctType.CreditCard);
			JArray result;
			DateTime minDate = DateTime.MaxValue;
			if (!string.IsNullOrWhiteSpace(file.Content)) {
				// They have uploaded a Qif file
				QifImporter qif = new QifImporter() {
					DateFormat = dateFormat
				};
				result = qif.ImportTransactions(new System.IO.StreamReader(file.Stream()), this);
				Utils.Check(result.Count > 0, "No transactions found");
				minDate = result.Min(i => (DateTime)i["Date"]);
			} else {
				// They have uploaded pasted data
				data = data.Replace("\r", "") + "\n";
				Utils.Check(!string.IsNullOrWhiteSpace(format), "You must enter a Statement Format");
				// See Import Help for details of format notation
				format = format.Replace("\r", "").Replace("\t", "{Tab}").Replace("\n", "{Newline}");
				string regex = format
					.Replace("{Tab}", @"\t")
					.Replace("{Newline}", @"\n");
				regex = Regex.Replace(regex, @"\{Any\}", delegate(Match m) {
					// Look at next character
					string terminator = regex.Substring(m.Index + m.Length, 1);
					switch (terminator) {
						case @"\n":
						case @"\t":
							break;
						default:
							// Terminate "ignore any" section at next newline or tab
							terminator = @"\t\n";
							break;
					}
					return @"[^" + terminator + @"]*?";
				});
				regex = Regex.Replace(regex, @"\{Optional:([^}]+)\}", "(?:$1)?");
				regex = Regex.Replace(regex, @"\{([^}]+)\}", delegate(Match m) {
					// Look at next character
					string terminator = m.Index + m.Length >= regex.Length ? "" : regex.Substring(m.Index + m.Length, 1);
					switch (terminator) {
						case @"\n":
						case @"\t":
							break;
						default:
							// Terminate field at next newline or tab
							terminator = @"\t\n";
							break;
					}
					// Create named group with name from inside {}
					return @"(?<" + m.Groups[1] + @">[^" + terminator + @"]*?)";
				});
				regex = "(?<=^|\n)" + regex;
				result = new JArray();
				Regex r = new Regex(regex, RegexOptions.Singleline);
				bool valid = false;
				foreach (Match m in r.Matches(data)) {
					JObject o = new JObject();
					string value = null;
					try {
						decimal amount = 0;
						foreach (string groupName in r.GetGroupNames()) {
							value = m.Groups[groupName].Value;
							switch (groupName) {
								case "0":
									break;
								case "Date":
									DateTime date = DateTime.Parse(value);
									if (date < minDate)
										minDate = date;
									o["Date"] = date;
									break;
								case "Amount":
									Utils.Check(extractAmount(value, ref amount), "Unrecognised Amount {0}", value);
									o["Amount"] = -amount;
									break;
								case "Payment":
									if (extractAmount(value, ref amount))
										o["Amount"] = -Math.Abs(amount);
									break;
								case "Deposit":
									if (extractAmount(value, ref amount))
										o["Amount"] = Math.Abs(amount);
									break;
								default:
									o[groupName] = value;
									break;
							}
						}
						Utils.Check(o["Amount"] != null, "No Payment, Deposit or Amount");
						Utils.Check(o["Date"] != null, "No Date");
						valid = true;
					} catch (Exception ex) {
						o["@class"] = "warning";
						o["Name"] = ex.Message + ":" + value + ":" + m.Value;
					}
					result.Add(o);
				}
				if (valid) {
					// The format was valid - save it to the account for next time
					account.StatementFormat = format;
					Database.Update(account);
				}
			}
			List<string> plus = new List<string>();
			List<string> minus = new List<string>();
			plus.Add(account.AccountTypeId == (int)AcctType.Bank ? "Deposit" : "Card Credit");
			minus.Add(account.AccountTypeId == (int)AcctType.Bank ? "Cheque" : "Card Charge");
			plus.Add("Transfer");
			minus.Add("Transfer");
			if (Database.QueryOne("SELECT idNameAddress FROM NameAddress WHERE Type = 'C'") != null)
				plus.Add("Customer Payment");
			if (Database.QueryOne("SELECT idNameAddress FROM NameAddress WHERE Type = 'S'") != null)
				minus.Add("Bill Payment");
			if (Database.QueryOne("SELECT idNameAddress FROM NameAddress WHERE Type = 'M'") != null)
				plus.Add("Subscriptions");
			JObject record = new JObject().AddRange(
					"import", result,
					"transactions", potentialMatches(id, minDate),
					"plus", plus,
					"minus", minus
				);
			// Save data to session
			SessionData.StatementImport = record;
			SessionData.Remove("StatementMatch");
			Redirect("/banking/statementmatching.html?id=" + id);
		}

		/// <summary>
		/// Extract monetary amount from a string. Return true if one was found
		/// </summary>
		static bool extractAmount(string a, ref decimal amount) {
			string dot = Regex.Escape(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.CurrencyDecimalSeparator);
			Match v = Regex.Match(a.Replace(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.CurrencyGroupSeparator, ""), 
				@"([^\d" + dot + @"]*)([\d" + dot + @"]+)");
			if (!v.Success)
				return false;
			amount = decimal.Parse(v.Groups[2].Value);
			a = v.Groups[1].Value;
			if(a.Contains("-") || a.Contains("CR"))
				amount = -amount;
			return true;
		}

		/// <summary>
		/// Return all possible potential matches for transactions after minDate - 7 days
		/// </summary>
		IEnumerable<JObject> potentialMatches(int id, DateTime minDate) {
			HashSet<string> existing = new HashSet<string>();
			minDate = minDate.AddDays(-7);
			foreach (dynamic doc in DetailListing(id)) {
				if (doc.DocumentDate >= minDate || doc.Clr != "X") {
					// Unreconciled transaction in date range - all these are possible matches
					yield return doc;
				} else {
					// Otherwise return 1 transaction for each unique Name, Type, Memo
					string key = doc.DocumentName + ":" + doc.DocumentTypeId + ":" + doc.DocumentMemo;
					if (existing.Contains(key))
						continue;
					existing.Add(key);
					yield return doc;
				}
			}
		}

		/// <summary>
		/// Return saved session data for statement matching
		/// </summary>
		[Auth(AccessLevel.ReadWrite, Hide = true)]
		public void StatementMatching() {
			Record = SessionData.StatementImport;
			SessionData.Remove("StatementMatch");
		}

		/// <summary>
		/// Update 1 matched transaction
		/// </summary>
		public AjaxReturn StatementMatchingSave(MatchInfo json) {
			checkAccountIsAcctType(json.id, AcctType.Bank, AcctType.CreditCard);
			JObject current = SessionData.StatementImport.import[json.current];
			Utils.Check(current != null, "Current not found");
			if (json.transaction >= 0) {
				Extended_Document transaction = SessionData.StatementImport.transactions[json.transaction].ToObject<Extended_Document>();
				checkDocType(transaction.DocumentTypeId,
					DocType.Payment,
					DocType.BillPayment,
					DocType.Cheque,
					DocType.Deposit,
					DocType.CreditCardCharge,
					DocType.CreditCardCredit,
					DocType.Transfer,
					DocType.Subscriptions);
			}
			// Save json to session
			SessionData.StatementMatch = json.ToJToken();
			return new AjaxReturn() { redirect = "statementmatch.html?id=" + json.id };
		}

		/// <summary>
		/// Update a matched transaction
		/// </summary>
		[Auth(AccessLevel.ReadWrite, Hide = true)]
		public void StatementMatch() {
			Utils.Check(SessionData.StatementMatch != null, "Invalid call to StatementMatch");
			MatchInfo match = SessionData.StatementMatch.ToObject<MatchInfo>();
			Account account = Database.Get<Account>(match.id);
			// The existing transaction to match (or empty record if none)
			Extended_Document transaction = match.transaction < 0 ? Database.EmptyRecord<Extended_Document>() : 
				SessionData.StatementImport.transactions[match.transaction].ToObject<Extended_Document>();
			// The statement transaction
			dynamic current = SessionData.StatementImport.import[match.current];
			Utils.Check(current != null, "No current transaction");
			bool same = match.type == "Same";
			bool documentHasVat = false;
			bool payment = false;
			decimal cAmount = current.Amount;
			int id = transaction.idDocument ?? 0;
			DocType type;
			if(match.transaction >= 0)
				type = (DocType)transaction.DocumentTypeId;
			else switch(match.type) {
					case "Deposit":
						Utils.Check(account.AccountTypeId == (int)AcctType.Bank, "Deposit not to bank account");
						type = DocType.Deposit;
						break;
					case "CardCredit":
						Utils.Check(account.AccountTypeId == (int)AcctType.CreditCard, "Credit not to credit card");
						type = DocType.CreditCardCredit;
						break;
					case "Transfer":
						type = DocType.Transfer;
						break;
					case "CustomerPayment":
						type = DocType.Payment;
						break;
					case "Subscriptions":
						type = DocType.Subscriptions;
						break;
					case "Cheque":
						Utils.Check(account.AccountTypeId == (int)AcctType.Bank, "Cheque not to bank account");
						type = DocType.Cheque;
						break;
					case "CardCharge":
						Utils.Check(account.AccountTypeId == (int)AcctType.CreditCard, "Charge not to credit card");
						type = DocType.CreditCardCharge;
						break;
					case "BillPayment":
						type = DocType.BillPayment;
						break;
					default:
						throw new CheckException("Unknown match type {0}", match.type);
				}
			GetParameters["acct"] = match.id.ToString();	// This bank account
			// Call appropriate method to get Record, and therefore transaction
			// Also set Module and Method, so appropriate template is used to display transaction before posting
			switch (type) {
				case DocType.Payment:
					Module = "customer";
					Method = "payment";
					Customer cust = new Customer() {
						CopyFrom = this
					};
					cust.Payment(id);
					this.Record = cust.Record;
					this.Form = cust.Form;
					payment = true;
					break;
				case DocType.BillPayment:
					Module = "supplier";
					Method = "payment";
					Supplier supp = new Supplier() {
						CopyFrom = this
					};
					supp.Payment(id);
					this.Record = supp.Record;
					this.Form = supp.Form;
					payment = true;
					break;
				case DocType.Cheque:
				case DocType.Deposit:
				case DocType.CreditCardCharge:
				case DocType.CreditCardCredit:
					Method = "document";
					Document(id, type);
					documentHasVat = true;
					break;
				case DocType.Transfer:
					Method = "transfer";
					Transfer(id);
					break;
				case DocType.Subscriptions:
					Module = "Members";
					Method = "document";
					Members member = new Members() {
						CopyFrom = this
					};
					member.Document(id);
					this.Record = member.Record;
					this.Form = member.Form;
					break;
				default:
					throw new CheckException("Unexpected document type:{0}", type.UnCamel());
			}
			dynamic record = (JObject)Record;
			dynamic doc = record.header;
			if (id == 0 && type == DocType.Transfer && cAmount > 0) {
				// New transfer in
				doc.TransferAccountId = match.id;
				doc.DocumentAccountId = 0;
				doc.DocumentAccountName = "";
			}
			if (string.IsNullOrWhiteSpace(doc.DocumentMemo.ToString())) {
				// Generate a memo
				string name = current.Name;
				string memo = current.Memo;
				if (string.IsNullOrWhiteSpace(memo))
					memo = name;
				else if (!memo.Contains(name))
					memo = name + " " + memo;
				doc.DocumentMemo = memo;
			}
			if (!same) {
				// They want to create a new document - try to guess the DocumentName
				string name = doc.DocumentName;
				string currentName = current.Name;
				currentName = currentName.Split('\n', '\t')[0];
				if (string.IsNullOrWhiteSpace(name) || (!payment && name.SimilarTo(currentName) < 0.5)) {
					doc.DocumentName = currentName;
					doc.DocumentNameAddressId = 0;
					foreach (NameAddress n in Database.Query<NameAddress>("SELECT * FROM NameAddress WHERE Type = 'O' AND Name <= "
						+ Database.Quote(currentName) + " AND Name LIKE " + Database.Quote(currentName.Substring(0, 5) + "%"))) {
							if (n.Name.SimilarTo(currentName) >= 0.5) {
								doc.DocumentName = n.Name;
								doc.DocumentNameAddressId = n.idNameAddress;
								break;
							}
					}
				}
			}
			doc.DocumentDate = current.Date;
			decimal tAmount = doc.DocumentAmount;
			decimal diff = Math.Abs(cAmount) - Math.Abs(tAmount);
			doc.DocumentAmount += diff;
			if(same)
				Utils.Check(diff == 0, "Amounts must be the same");
			else {
				// New transaction
				doc.DocumentOutstanding = doc.DocumentAmount;
				doc.Clr = "";
				doc.idDocument = doc.Id = null;
				if (Utils.ExtractNumber(doc.DocumentIdentifier.ToString()) > 0)
					doc.DocumentIdentifier = "<next>";
			}
			if(string.IsNullOrEmpty(doc.DocumentIdentifier.ToString())) {
				if (current.Id != null) {
					doc.DocumentIdentifier = current.Id;
				} else {
					int no = Utils.ExtractNumber(current.Name.ToString());
					if (no != 0)
						doc.DocumentIdentifier = no.ToString();
				}
			}
			if (diff != 0 && documentHasVat) {
				// Adjust first line to account for difference
				if (record.detail.Count == 0)
					record.detail.Add(new InvoiceLine().ToJToken());
				dynamic line = record.detail[0];
				decimal val = line.LineAmount + line.VatAmount + diff;
				if (line.VatRate != 0) {
					line.VatAmount = Math.Round(val * line.VatRate / (100 + line.VatRate), 2);
					val -= line.VatAmount;
				}
				line.LineAmount = val;
			}
			if (payment && !same)
				removePayments(record);
			record.StatementAccount = match.id;
			if (same) {
				// Just post the new information
				if (type == DocType.Transfer)
					record = record.header;		// Transfer posts header alone
				AjaxReturn p = StatementMatchSave((JObject)record);
				if (p.error == null)
					Redirect(p.redirect);		// If no error, go on with matching
			}
		}

		void removePayments(dynamic record) {
			record.header.Allocated = 0M;
			record.header.Remaining = (decimal)record.header.DocumentAmount;
			int l = record.detail.Count;
			while(l-- > 0) {
				dynamic line = record.detail[l];
				decimal amountPaid = line.AmountPaid;
				if (amountPaid != 0) {
					decimal outstanding = line.Outstanding - amountPaid;
					if (outstanding == 0)
						record.detail.RemoveAt(l);
					else
						line.Outstanding = outstanding;
				}
			}
		}

		/// <summary>
		/// Save a matched transaction.
		/// May be called direct from StatementMatch for Same transactions,
		/// or when the user presses "Save" for other transactions
		/// </summary>
		public AjaxReturn StatementMatchSave(JObject json) {
			Utils.Check(SessionData.StatementMatch != null, "Invalid call to StatementMatchSave");
			MatchInfo match = SessionData.StatementMatch.ToObject<MatchInfo>();
			JArray transactions = SessionData.StatementImport.transactions;
			dynamic transaction = match.transaction < 0 ? null : SessionData.StatementImport.transactions[match.transaction];
			DocType type = match.transaction < 0 ? match.type == "Transfer" ? DocType.Transfer : DocType.Cheque : 
				(DocType)((JObject)transactions[match.transaction]).AsInt("DocumentTypeId");
			AjaxReturn result;
			switch (type) {
				case DocType.Payment:
					result = new Customer() {
						Context = Context,
						GetParameters = GetParameters,
						PostParameters = PostParameters,
						Parameters = Parameters,
					}.PaymentSave(json.To<CustomerSupplier.PaymentDocument>());
					break;
				case DocType.BillPayment:
					result = new Supplier() {
						Context = Context,
						GetParameters = GetParameters,
						PostParameters = PostParameters,
						Parameters = Parameters,
					}.PaymentSave(json.To<CustomerSupplier.PaymentDocument>());
					break;
				case DocType.Cheque:
				case DocType.Deposit:
				case DocType.CreditCardCharge:
				case DocType.CreditCardCredit:
					result = DocumentSave(json.To<BankingDocument>());
					break;
				case DocType.Transfer:
					result = TransferSave(json.To<TransferDocument>());
					break;
				case DocType.Subscriptions:
					result = new Members() {
						Context = Context,
						GetParameters = GetParameters,
						PostParameters = PostParameters,
						Parameters = Parameters,
					}.DocumentSave(json.To<Members.SubscriptionDocument>());
					break;
				default:
					throw new CheckException("Unexpected document type:{0}", type.UnCamel());
			}
			if (result.error == null) {
				if(match.transaction >= 0 && match.type == "Same")
					transaction.Matched = 1;
				JArray items = SessionData.StatementImport.import;
				items.RemoveAt(match.current);
				result.redirect = "/banking/" + (items.Count == 0 ? "detail" : "statementmatching") + ".html?id=" + match.id;
			}
			return result;
		}

		public class MatchInfo : JsonObject {
			/// <summary>
			/// Bank account id
			/// </summary>
			public int id;
			/// <summary>
			/// New, Transfer or Same
			/// </summary>
			public string type;
			/// <summary>
			/// Index of chosen record from statement
			/// </summary>
			public int current;
			/// <summary>
			/// Index of chosen transaction from matched transactions, or -1 if none
			/// </summary>
			public int transaction;
		}

		public class BankingDetail : Account {
			public decimal? Balance;
			public decimal? CurrentBalance;
		}

		public class BankingDocument : JsonObject {
			public Extended_Document header;
			public List<InvoiceLine> detail;
		}

		public class ReconcileHeader : Account {
			public decimal OpeningBalance;
			public decimal ClearedBalance;
		}

		public class ReconcileLine : Extended_Document {
			public int idJournal;
			public string Cleared;
			public decimal Amount;
		}

		public class ReconcileDocument {
			public bool Temporary;
			public ReconcileHeader header;
			public ReconcileLine[] detail;
			public bool print;
		}

	}

	public class FullAccount : Account {
		public string AcctType;

		public int NextNumber(DocType docType) {
			switch (docType) {
				case DocType.Cheque:
				case DocType.CreditCardCharge:
					return NextChequeNumber;
				case DocType.Deposit:
				case DocType.CreditCardCredit:
					return NextDepositNumber;
				default:
					return 0;
			}
		}

		public bool RegisterNumber(DocType docType, int current) {
			switch (docType) {
				case DocType.Cheque:
				case DocType.CreditCardCharge:
					return registerNumber(ref NextChequeNumber, current);
				case DocType.Deposit:
				case DocType.CreditCardCredit:
					return registerNumber(ref NextDepositNumber, current);
			}
			return false;
		}

		bool registerNumber(ref int next, int current) {
			if (current >= next) {
				next = current + 1;
				return true;
			}
			return false;
		}


	}
}
