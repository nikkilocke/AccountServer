using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	public class Supplier : CustomerSupplier {

		public Supplier()
			: base("S", Acct.PurchaseLedger, DocType.Bill, DocType.Credit, DocType.BillPayment) {
		}

		public object DetailListing(int id) {
			return Database.Query("Document.*, DocType, -Amount AS Amount, -Outstanding AS Outstanding, Login AS AuthorisedBy",
				"WHERE AccountId = " + (int)LedgerAccount + " AND NameAddressId = " + id + " ORDER BY DocumentDate DESC, idDocument",
				"Document", "Journal", "User");
		}

		public void Document(int id, DocType type) {
			JObject record = document(id, type);
			record["detail"] = Database.Query("idJournal, DocumentId, Line.VatCodeId, VatRate, JournalNum, Journal.AccountId, Memo, Qty, LineAmount, VatAmount",
					"WHERE Journal.DocumentId = " + id + " AND idLine IS NOT NULL ORDER BY JournalNum",
					"Document", "Journal", "Line");
			record.Add("Accounts", SelectAccounts());
			Record = record;
		}

		protected override void calculatePaymentChanges(PaymentDocument json, decimal amount, out decimal changeInDocumentAmount, out decimal changeInOutstanding) {
			PaymentHeader document = json.header;
			PaymentHeader original = getDocument(document);
			changeInDocumentAmount = -(document.DocumentAmount - original.DocumentAmount);
			changeInOutstanding = 0;
			Utils.Check(-changeInDocumentAmount == amount, "Change in document amount {0:0.00} does not agree with payments {1:0.00}",
				-changeInDocumentAmount, amount);
		}

	}

}

