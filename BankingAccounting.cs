using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	/// <summary>
	/// Common functionality from Banking & Accounting
	/// </summary>
	public class BankingAccounting : AppModule {

		public BankingAccounting() {
		}

		public void Names() {
			// When maintaining names, use the template in banking
			Module = "banking";
		}

		public object NamesListing() {
			return Database.Query("SELECT * FROM NameAddress WHERE Type = 'O' ORDER BY Name");
		}

		public void Name(int id) {
			// When maintaining names, use the template in banking
			Module = "banking";
			NameAddress record = Database.Get<NameAddress>(id);
			// Can only maintain Other names here
			if (record.Id == null)
				record.Type = "O";
			else {
				checkNameType(record.Type, "O");
				Title += " - " + record.Name;
			}
			Record = record;
		}

		public AjaxReturn NamePost(NameAddress json) {
			checkNameType(json.Type, "O");
			return PostRecord(json, true);
		}

		/// <summary>
		/// Get an existing transfer document, or fill in a new one
		/// </summary>
		internal TransferDocument GetTransferDocument(int id) {
			TransferDocument header = getDocument<TransferDocument>(id);
			int? acct = GetParameters["acct"].IsInteger() ? Parameters.AsInt("acct") : (int?)null;
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)DocType.Transfer;
				header.DocType = DocType.Transfer.UnCamel();
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				header.DocumentMemo = "Money Transfer";
				if (acct != null)
					header.DocumentAccountId = (int)acct;
			} else {
				checkDocType(header.DocumentTypeId, DocType.Transfer);
				header.TransferAccountId = Database.QueryOne("SELECT AccountId FROM Journal WHERE DocumentId = " + id + " AND JournalNum = 2").AsInt("AccountId");
				checkAccountIsAcctType(header.DocumentAccountId, AcctType.Bank, AcctType.CreditCard, AcctType.Investment);
				checkAccountIsAcctType(header.TransferAccountId, AcctType.Bank, AcctType.CreditCard, AcctType.Investment);
				if (acct == null)
					acct = header.DocumentAccountId;
			}
			return header;
		}

		public void Transfer(int id) {
			// Use template in banking
			Module = "banking";
			TransferDocument header = GetTransferDocument(id);
			int? acct = GetParameters["acct"].IsInteger() ? Parameters.AsInt("acct") : (int?)null;
			JObject record = new JObject().AddRange("header", header,
				"Account", acct,
				"BankAccounts", new Select().BankOrStockAccount(""));
			if (acct != null)
				Database.NextPreviousDocument(record, "JOIN Journal ON DocumentId = idDocument WHERE AccountId = "
					+ acct + " AND DocumentTypeId = " + (int)DocType.Transfer);
			Record = record;
		}

		public AjaxReturn TransferPost(TransferDocument json) {
			Database.BeginTransaction();
			checkDocType(json.DocumentTypeId, DocType.Transfer);
			checkAccountIsAcctType(json.DocumentAccountId, AcctType.Bank, AcctType.CreditCard, AcctType.Investment);
			checkAccountIsAcctType(json.TransferAccountId, AcctType.Bank, AcctType.CreditCard, AcctType.Investment);
			fixNameAddress(json, "O");
			JObject oldDoc = getCompleteDocument(json.idDocument);
			Database.Update(json);
			// Transfer has 2 journals, 1 line, no VAT
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)json.idDocument,
				JournalNum = 1
			});
			journal.DocumentId = (int)json.idDocument;
			journal.AccountId = json.DocumentAccountId;
			journal.NameAddressId = json.DocumentNameAddressId;
			journal.Amount = -json.DocumentAmount;
			journal.Outstanding = -json.DocumentAmount;
			journal.Memo = json.DocumentMemo;
			journal.JournalNum = 1;
			Database.Update(journal);
			journal = Database.Get(new Journal() {
				DocumentId = (int)json.idDocument,
				JournalNum = 2
			});
			journal.DocumentId = (int)json.idDocument;
			journal.AccountId = (int)json.TransferAccountId;
			journal.NameAddressId = json.DocumentNameAddressId;
			journal.Amount = json.DocumentAmount;
			journal.Outstanding = json.DocumentAmount;
			journal.Memo = json.DocumentMemo;
			journal.JournalNum = 2;
			Database.Update(journal);
			Line line = Database.Get(new Line() {
				idLine = journal.idJournal
			});
			line.idLine = journal.idJournal;
			line.LineAmount = json.DocumentAmount;
			Database.Update(line);
			Database.Update(json);
			JObject newDoc = getCompleteDocument(json.idDocument);
			Database.AuditUpdate("Document", json.idDocument, oldDoc, newDoc);
			Database.Commit();
			return new AjaxReturn() { message = "Transfer saved", id = json.idDocument };
		}

		public AjaxReturn TransferDelete(int id) {
			return deleteDocument(id, DocType.Transfer);
		}

		public class TransferDocument : Extended_Document {
			public int? TransferAccountId;
		}

	}
}
