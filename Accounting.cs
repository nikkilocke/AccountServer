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
	/// Accounting module has some functionality in common with Banking (e.g. NameAddress maintenance)
	/// </summary>
	public class Accounting : BankingAccounting {
		public Accounting() {
			Menu = new MenuOption[] {
				new MenuOption("List Accounts", "/accounting/default.html"),
				new MenuOption("List Journals", "/accounting/journals.html"),
				new MenuOption("Names", "/accounting/names.html"),
				new MenuOption("VAT Return", "/accounting/vatreturn.html?id=0"),
				new MenuOption("New Account", "/accounting/detail.html?id=0"),
				new MenuOption("New Journal", "/accounting/document.html?id=0")
			};
		}

		/// <summary>
		/// List all non-protected accounts
		/// </summary>
		public object DefaultListing() {
			return Database.Query("*",
				"WHERE Protected = 0 ORDER BY AccountTypeId, AccountName",
				"Account");
		}

		/// <summary>
		/// Retrieve data for editing an account
		/// </summary>
		public void Detail(int id) {
			FullAccount account = Database.Get<FullAccount>(id);
			Utils.Check(!account.Protected, "Cannot edit a protected account");
			if (account.Id != null)
				Title += " - " + account.AccountName;
			Record = new JObject().AddRange(
				"header", account, 
				"AccountTypes", SelectAccountTypes(),
				"Transactions", Database.QueryOne("SELECT idJournal FROM Journal WHERE AccountId = " + id) != null
				);
		}

		/// <summary>
		/// List all journals for this account
		/// </summary>
		public IEnumerable<Extended_Document> DetailListing(int id) {
			Extended_Document last = null;
			int lastId = 0;
			foreach (JObject l in Database.Query(@"SELECT Journal.idJournal, Document.*, NameAddress.Name AS DocumentName, DocType, Journal.Cleared, Journal.Amount AS DocumentAmount, AccountName AS DocumentAccountName
FROM Journal
LEFT JOIN Document ON idDocument = Journal.DocumentId
LEFT JOIN DocumentType ON DocumentType.idDocumentType = Document.DocumentTypeId
LEFT JOIN NameAddress ON NameAddress.idNameAddress = Journal.NameAddressId
LEFT JOIN Journal AS J ON J.DocumentId = Journal.DocumentId AND J.AccountId <> Journal.AccountId
LEFT JOIN Account ON Account.idAccount = J.AccountId
WHERE Journal.AccountId = " + id + @"
ORDER BY DocumentDate DESC, idDocument DESC")) {
				Extended_Document line = l.To<Extended_Document>();
				if (last != null) {
					if (lastId == l.AsInt("idJournal")) {
						last.DocumentAccountName = "-split-";
						continue;
					}
					yield return last;
					last = null;
				}
				last = line;
				lastId = l.AsInt("idJournal");
			}
			if (last != null)
				yield return last;
		}

		public AjaxReturn DetailDelete(int id) {
			Database.BeginTransaction();
			FullAccount account = Database.Get<FullAccount>(id);
			Utils.Check(account.idAccount == id, "Account not found");
			Utils.Check(!account.Protected, "Cannot delete a protected account");
			Utils.Check(Database.QueryOne("SELECT idJournal FROM Journal WHERE AccountId = " + id) == null, "Cannot delete - there are transactions");
			Database.Delete("Account", id, true);
			Database.Commit();
			return new AjaxReturn() { redirect = "/accounting/default.html" };
		}

		/// <summary>
		/// Update an account after editing.
		/// </summary>
		public AjaxReturn DetailPost(Account json) {
			Account existing = Database.Get(json);
			Utils.Check(!existing.Protected, "Cannot edit a protected account");
			Database.BeginTransaction();
			AjaxReturn result = PostRecord(json, true);
			if (string.IsNullOrEmpty(result.error) && existing.idAccount > 0 && json.AccountName != existing.AccountName) {
				// This might be a parent account - if so change the name of subaccounts
				foreach(Account a in Database.Query<Account>("SELECT * FROM Account WHERE AccountName LIKE "
					+ Database.Quote(existing.AccountName + ":%"))) {
					if(a.AccountName.StartsWith(json.AccountName + ":")) {
						a.AccountName = json.AccountName + a.AccountName.Substring(json.AccountName.Length);
						Database.Update(a);
					}
				}
			}
			Database.Commit();
			return result;
		}

		public class JournalDoc : JsonObject {
			[Primary(AutoIncrement = false)]
			public int? idDocument;
			public DateTime DocumentDate;
			public string DocumentIdentifier;
			[Length(0)]
			public string DocumentMemo;
		}

		public void Journals() {
			DataTableForm form = new DataTableForm(this, typeof(JournalDoc));
			form["idDocument"].Visible = false;
			form.Options["table"] = "Document";
			form.Select = "/accounting/document.html";
			Form = form;
			form.Show();
		}

		public IEnumerable<JournalDoc> JournalsListing() {
			return Database.Query<JournalDoc>(@"SELECT idDocument, DocumentDate, DocumentIdentifier, DocumentName, DocumentMemo 
FROM Extended_Document
WHERE DocumentTypeId = " + (int)DocType.GeneralJournal + @"
ORDER BY DocumentDate DESC, idDocument DESC");
		}

		/// <summary>
		/// Retrieve a General Ledger Journal by id, for editing
		/// </summary>
		public void Document(int id) {
			Extended_Document header = getDocument<Extended_Document>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)DocType.GeneralJournal;
				header.DocType = DocType.GeneralJournal.UnCamel();
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				header.DocumentIdentifier = Settings.NextJournalNumber.ToString();
				if (GetParameters["acct"].IsInteger()) {
					FullAccount acct = Database.QueryOne<FullAccount>("*", "WHERE idAccount = " + GetParameters["acct"], "Account");
					if (acct.idAccount != null) {
						header.DocumentAccountId = (int)acct.idAccount;
						header.DocumentAccountName = acct.AccountName;
					}
				}
			} else {
				checkDocType(header.DocumentTypeId, DocType.GeneralJournal);
			}
			JObject record = new JObject().AddRange("header", header,
				"detail", Database.Query("idJournal, DocumentId, JournalNum, AccountId, Memo, Amount, NameAddressId, Name",
					"WHERE Journal.DocumentId = " + id + " ORDER BY JournalNum",
					"Document", "Journal"));
			Database.NextPreviousDocument(record, "JOIN Journal ON DocumentId = idDocument WHERE DocumentTypeId = " + (int)DocType.GeneralJournal
				+ (header.DocumentAccountId > 0 ? " AND AccountId = " + header.DocumentAccountId : ""));
			record.AddRange("Accounts", SelectAllAccounts(),
				"VatCodes", SelectVatCodes(),
				"Names", SelectNames());
			Record = record;
		}

		/// <summary>
		/// Delete a General Ledger Journal
		/// </summary>
		public AjaxReturn DocumentDelete(int id) {
			return deleteDocument(id, DocType.GeneralJournal);
		}

		/// <summary>
		/// Update a General Ledger Journal after editing.
		/// </summary>
		public AjaxReturn DocumentPost(JournalDocument json) {
			Database.BeginTransaction();
			Extended_Document document = json.header;
			JObject oldDoc = getCompleteDocument(document.idDocument);
			checkDocType(document.DocumentTypeId, DocType.GeneralJournal);
			allocateDocumentIdentifier(document);
			decimal total = 0, vat = 0;
			int lineNum = 1;
			Database.Update(document);
			Settings.RegisterNumber(this, (int?)DocType.GeneralJournal, Utils.ExtractNumber(document.DocumentIdentifier));
			// Find any existing VAT record
			Journal vatJournal = Database.QueryOne<Journal>("SELECT * FROM Journal WHERE DocumentId = " + document.idDocument
				+ " AND AccountId = " + (int)Acct.VATControl + " ORDER BY JournalNum DESC");
			JournalDetail vatDetail = null;
			if (vatJournal.idJournal != null)
				Database.Delete("Journal", (int)vatJournal.idJournal, false);
			foreach (JournalDetail detail in json.detail) {
				if (detail.AccountId == 0) continue;
				total += detail.Amount;
				if (detail.AccountId == (int)Acct.VATControl) {
					// Vat has to all be posted on the last line
					vatDetail = detail;
					vat += detail.Amount;
					continue;
				}
				// Get existing journal (if any)
				Journal journal = Database.Get(new Journal() {
					DocumentId = (int)document.idDocument,
					JournalNum = lineNum
				});
				detail.Id = journal.Id;
				detail.DocumentId = (int)document.idDocument;
				detail.JournalNum = lineNum;
				if (detail.NameAddressId == null || detail.NameAddressId == 0) {
					detail.NameAddressId = string.IsNullOrWhiteSpace(detail.Name) ?
						1 :
						Database.ForeignKey("NameAddress",
											"Type", "O",
											"Name", detail.Name);
				}
				// Change outstanding by the change in the amount
				detail.Outstanding = journal.Outstanding + detail.Amount - journal.Amount;
				Database.Update(detail);
				if (lineNum > 1) {
					// Create a dummy line record
					Line line = new Line() {
						idLine = detail.idJournal,
						Qty = 0,
						LineAmount = -detail.Amount,
						VatCodeId = null,
						VatRate = 0,
						VatAmount = 0
					};
					Database.Update(line);
				}
				lineNum++;
			}
			Utils.Check(total == 0, "Journal does not balance by {0}", total);
			// Delete any lines and journals that were in the old version, but not in the new
			Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum + ")");
			Database.Execute("DELETE FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum);
			if (vat != 0 || vatJournal.idJournal != null) {
				// There is, or was, a posting to vat
				decimal changeInVatAmount = vat - vatJournal.Amount;
				vatJournal.DocumentId = (int)document.idDocument;
				vatJournal.AccountId = (int)Acct.VATControl;
				if (vatDetail != null) {
					if ((vatDetail.NameAddressId == null || vatDetail.NameAddressId == 0) && !string.IsNullOrWhiteSpace(vatDetail.Name)) {
						vatJournal.NameAddressId = Database.ForeignKey("NameAddress",
												"Type", "O",
												"Name", vatDetail.Name);
					} else {
						vatJournal.NameAddressId = vatDetail.NameAddressId;
					}
				}
				if(vatJournal.NameAddressId == null || vatJournal.NameAddressId == 0)
					vatJournal.NameAddressId = 1;
				vatJournal.Memo = "Total VAT";
				vatJournal.JournalNum = lineNum++;
				vatJournal.Amount = vat;
				vatJournal.Outstanding += changeInVatAmount;
				Database.Update(vatJournal);
			}
			// Audit the change
			JObject newDoc = getCompleteDocument(document.idDocument);
			Database.AuditUpdate("Document", document.idDocument, oldDoc, newDoc);
			Settings.RegisterNumber(this, document.DocumentTypeId, Utils.ExtractNumber(document.DocumentIdentifier));
			Database.Commit();
			return new AjaxReturn() { message = "Journal saved", id = document.idDocument };
		}

		/// <summary>
		/// Retrieve a VAT return for review.
		/// </summary>
		/// <param name="id">A specific VAT return, or 0 to get one for last quarter</param>
		public void VatReturn(int id) {
			// Find the VAT payment to HMRC
			// It will be a cheque, credit, or credit card equivalent
			// Journal line 2 will be to VAT control
			// If no id provided, get the most recently posted one
			Extended_Document header = Database.QueryOne<Extended_Document>(@"SELECT Extended_Document.*
FROM Extended_Document
JOIN Journal ON DocumentId = idDocument
WHERE AccountId = " + (int)Acct.VATControl + @"
AND JournalNum = 2
AND DocumentTypeId " + Database.In(DocType.Cheque, DocType.Deposit, DocType.CreditCardCharge, DocType.CreditCardCredit)
				+ (id == 0 ? "" : "AND idDocument = " + id) + @"
ORDER BY idDocument DESC");
			if (header.idDocument == null) {
				Utils.Check(id == 0, "VAT return " + id + " not found");
				header.DocumentNameAddressId = 1;
				header.DocumentName = "";
				if (Settings.DefaultBankAccount > 0) {
					Account acc = Database.QueryOne<Account>("*", "WHERE idAccount = " + Settings.DefaultBankAccount, "Account");
					if (acc.idAccount != null) {
						header.DocumentAccountId = (int)acc.idAccount;
						header.DocumentAccountName = acc.AccountName;
					}
				}
			}
			// If most recent VAT return is not for this quarter, we will create a new one (later, on save)
			if (id == 0 && header.DocumentDate < Settings.QuarterStart(Utils.Today))
				header.idDocument = null;
			if(header.idDocument == null)
				header.DocumentDate = Utils.Today;
			VatReturnDocument record = getVatReturn(header.idDocument, header.DocumentDate);
			if (header.idDocument == null) {
				header.DocumentMemo = "VAT - FROM " + record.Start.ToString("d") + " To " + record.End.ToString("d");
				header.DocumentDate = record.Due;
			}
			Record = new JObject().AddRange("return", record, 
				"payment", header,
				"names", SelectOthers(),
				"accounts", SelectBankAccounts(),
				"otherReturns", SelectVatPayments().Reverse()
				);
		}

		/// <summary>
		/// Update a VAt Return after review
		/// </summary>
		public AjaxReturn VatReturnPost(JObject json) {
			Database.BeginTransaction();
			Extended_Document header = json["payment"].To<Extended_Document>();
			Utils.Check(header.idDocument == null, "Cannot amend existing VAT return");
			// Need to go to and back from json to normalize numbers
			VatReturnDocument record = getVatReturn(null, Utils.Today).ToString().JsonTo<VatReturnDocument>();
			VatReturnDocument r = json["return"].To<VatReturnDocument>();
			Utils.Check(record.ToString() == r.ToString(), 
				"Another user has changed the VAT data - please refresh the page to get the latest data");
			FullAccount acct = Database.Get<FullAccount>((int)header.DocumentAccountId);
			allocateDocumentIdentifier(header, acct);
			fixNameAddress(header, "O");
			decimal toPay = record.ToPay;
			DocType t;
			switch ((AcctType)acct.AccountTypeId) {
				case AcctType.Bank:
					t = toPay < 0 ? DocType.Deposit : DocType.Cheque;
					break;
				case AcctType.CreditCard:
					t = toPay < 0 ? DocType.CreditCardCredit : DocType.CreditCardCharge;
					break;
				default:
					throw new CheckException("Account missing or invalid");
			}
			header.DocumentTypeId = (int)t;
			Database.Insert(header);
			int nextDocid = Utils.ExtractNumber(header.DocumentIdentifier);
			if (nextDocid > 0 && acct.RegisterNumber(t, nextDocid))
				Database.Update(acct);
			// Flag this document as part of this VAT return
			header.VatPaid = header.idDocument;
			Database.Update(header);
			Journal journal = new Journal() {
				DocumentId = (int)header.idDocument,
				AccountId = header.DocumentAccountId,
				NameAddressId = header.DocumentNameAddressId,
				Memo = header.DocumentMemo,
				JournalNum = 1,
				Amount = -toPay,
				Outstanding = -toPay
			};
			Database.Insert(journal);
			journal.idJournal = null;
			journal.AccountId = (int)Acct.VATControl;
			journal.JournalNum = 2;
			journal.Amount = toPay;
			journal.Outstanding = toPay;
			Database.Insert(journal);
			Line line = new Line() {
				idLine = journal.idJournal,
				LineAmount = toPay
			};
			Database.Insert(line);
			// Flag all documents from last quarter as part of this VAT return
			Database.Execute(@"UPDATE Document
JOIN Vat_Journal ON Vat_Journal.idDocument = Document.idDocument
SET Document.VatPaid = " + header.idDocument + @"
WHERE (Document.VatPaid IS NULL OR Document.VatPaid < 1)
AND Document.DocumentDate < " + Database.Quote(Settings.QuarterStart(Utils.Today)));
			JObject newDoc = getCompleteDocument(header.idDocument);
			Database.AuditUpdate("Document", header.idDocument, null, newDoc);
			Settings.RegisterNumber(this, header.DocumentTypeId, Utils.ExtractNumber(header.DocumentIdentifier));
			Database.Commit();
			return new AjaxReturn() { message = "Vat paid", id = header.idDocument };
		}

		/// <summary>
		/// Get VAT return data for a specific VAT return (id != null) or the last quarter ending before date (id == null)
		/// </summary>
		VatReturnDocument getVatReturn(int? id, DateTime date) {
			VatReturnDocument record = new VatReturnDocument();
			DateTime qe = Settings.QuarterStart(date);
			record.Start = qe.AddMonths(-3);
			record.End = qe.AddDays(-1);
			record.Due = qe.AddMonths(1).AddDays(-1);
			foreach (JObject r in Database.Query(@"SELECT VatType, SUM(VatAmount) AS Vat, SUM(LineAmount) AS Net
FROM Vat_Journal
JOIN VatCode ON idVatCode = VatCodeId
WHERE " + (id == null ? "(VatPaid IS NULL OR VatPaid < 1) AND DocumentDate < " + Database.Quote(qe) : "VatPaid = " + id) + @"
GROUP BY VatType")) {
					 switch (r.AsInt("VatType")) {
						 case -1:
							 record.Sales = r.ToObject<VatReturnLine>();
							 break;
						 case 1:
							 record.Purchases = r.ToObject<VatReturnLine>();
							 break;
						 default:
							 throw new CheckException("Invalid VatType {0}", r["VatType"]);
					 }
			}
			return record;
		}

		public class JournalDetail : Journal {
			public string Name;
		}

		public class JournalDocument : JsonObject {
			public Extended_Document header;
			public List<JournalDetail> detail;
		}

		public class VatReturnLine {
			/// <summary>
			/// -1 for Sales, 1 for Purchases
			/// </summary>
			public int VatType;
			public decimal Vat;
			public decimal Net;
		}

		public class VatReturnDocument : JsonObject {
			public VatReturnDocument() {
				Sales = new VatReturnLine() { VatType = -1 };
				Purchases = new VatReturnLine() { VatType = 1 };
			}
			public VatReturnLine Sales;
			public VatReturnLine Purchases;
			public DateTime Start;
			public DateTime End;
			public DateTime Due;
			public decimal ToPay {
				get {
					return Sales.Vat - Purchases.Vat;
				}
			}
		}

	}
}
