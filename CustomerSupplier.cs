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
	/// Common code for customer & supplier
	/// </summary>
	public abstract class CustomerSupplier : AppModule {
		/// <summary>
		/// "S" for supplier, "C" for customer
		/// </summary>
		public string NameType;
		/// <summary>
		/// "Customer" or "Supplier"
		/// </summary>
		public string Name;
		/// <summary>
		/// The different document types
		/// </summary>
		public DocType InvoiceDoc, CreditDoc, PaymentDoc;
		/// <summary>
		/// Purchase Ledger or Sales Ledger
		/// </summary>
		public Acct LedgerAccount;

		public CustomerSupplier(string nameType, Acct ledgerAccount, DocType invoiceDoc, DocType creditDoc, DocType paymentDoc) {
			NameType = nameType;
			Name = NameType.NameType();
			LedgerAccount = ledgerAccount;
			InvoiceDoc = invoiceDoc;
			CreditDoc = creditDoc;
			PaymentDoc = paymentDoc;
			string module = nameType == "C" ? "/customer/" : "/supplier/";
			Menu = new MenuOption[] {
				new MenuOption("Listing", module + "default.html"),
				new MenuOption("VAT codes", module + "vatcodes.html"),
				new MenuOption("New " + Name, module + "detail.html?id=0"),
				new MenuOption("New " + InvoiceDoc.UnCamel(), module + "document.html?id=0&type=" + (int)InvoiceDoc),
				new MenuOption("New " + CreditDoc.UnCamel(), module + "document.html?id=0&type=" + (int)CreditDoc),
				new MenuOption("New " + PaymentDoc.UnCamel(), module + "payment.html?id=0")
			};
		}

		/// <summary>
		/// All Customers or Suppliers
		/// </summary>
		public object DefaultListing() {
			return Database.Query(@"SELECT NameAddress.*, Count(Outstanding) AS Outstanding
FROM NameAddress
LEFT JOIN Journal ON NameAddressId = idNameAddress
AND AccountId = " + (int)LedgerAccount + @"
AND Outstanding <> 0
WHERE Type=" + Database.Quote(NameType) + @"
GROUP BY idNameAddress
ORDER BY Name
");
		}

		/// <summary>
		/// Get record for editing
		/// </summary>
		public void Detail(int id) {
			RecordDetail record = Database.QueryOne<RecordDetail>(@"SELECT NameAddress.*, Sum(Outstanding) AS Outstanding
FROM NameAddress
LEFT JOIN Journal ON NameAddressId = idNameAddress
AND AccountId = " + (int)LedgerAccount + @"
AND Outstanding <> 0
WHERE idNameAddress = " + id
);
			if (record.Id == null)
				record.Type = NameType;
			else {
				checkNameType(record.Type, NameType);
				addNameToMenuOptions((int)record.Id);
				Title += " - " + record.Name;
			}
			Record = record;
		}

		public AjaxReturn DetailPost(NameAddress json) {
			checkNameType(json.Type, NameType);
			return PostRecord(json, true);
		}

		/// <summary>
		/// Retrieve document, or prepare new one
		/// </summary>
		public JObject document(int id, DocType type) {
			Title = Title.Replace("Document", type.UnCamel());
			Extended_Document header = getDocument<Extended_Document>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)type;
				header.DocType = type.UnCamel();
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				header.DocumentIdentifier = Settings.NextNumber(type).ToString();
				if (GetParameters["name"].IsInteger()) {
					JObject name = Database.QueryOne("*", "WHERE Type = " + Database.Quote(NameType) + " AND idNameAddress = " + GetParameters["name"], "NameAddress");
					if (name != null) {
						checkNameType(name.AsString("Type"), NameType);
						header.DocumentNameAddressId = name.AsInt("idNameAddress");
						header.DocumentAddress = name.AsString("Address");
						header.DocumentName = name.AsString("Name");
					}
				}
			} else {
				checkDocType(header.DocumentTypeId, type);
				checkNameType(header.DocumentNameAddressId, NameType);
			}
			JObject record = new JObject().AddRange("header", header);
			Database.NextPreviousDocument(record, "WHERE DocumentTypeId = " + (int)type);
			record.AddRange("VatCodes", SelectVatCodes(),
				"Names", SelectNames(NameType));
			return record;
		}

		/// <summary>
		/// Update a document after editing
		/// </summary>
		public AjaxReturn DocumentPost(InvoiceDocument json) {
			Database.BeginTransaction();
			Extended_Document document = json.header;
			DocType t = checkDocType(document.DocumentTypeId, InvoiceDoc, CreditDoc);
			JObject oldDoc = getCompleteDocument(document.idDocument);
			int sign = SignFor(t);
			Extended_Document original = getDocument(document);
			decimal vat = 0;
			decimal net = 0;
			if (document.idDocument == null)
				allocateDocumentIdentifier(document);
			foreach (InvoiceLine detail in json.detail) {
				if ((detail.ProductId == 0 || detail.ProductId == null)
						&& (detail.AccountId == 0 || detail.AccountId == null))
					continue;
				net += detail.LineAmount;
				vat += detail.VatAmount;
			}
			Utils.Check(document.DocumentAmount == net + vat, "Document does not balance");
			decimal changeInDocumentAmount = -sign * (document.DocumentAmount - original.DocumentAmount);
			var lineNum = 1;
			fixNameAddress(document, NameType);
			Database.Update(document);
			// Find any existing VAT record
			Journal vatJournal = Database.QueryOne<Journal>("SELECT * FROM Journal WHERE DocumentId = " + document.idDocument
				+ " AND AccountId = " + (int)Acct.VATControl + " ORDER BY JournalNum DESC");
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)document.idDocument,
				JournalNum = lineNum
			});
			journal.DocumentId = (int)document.idDocument;
			journal.JournalNum = lineNum++;
			journal.AccountId = (int)LedgerAccount;
			journal.NameAddressId = document.DocumentNameAddressId;
			journal.Memo = document.DocumentMemo;
			journal.Amount += changeInDocumentAmount;
			journal.Outstanding += changeInDocumentAmount;
			Database.Update(journal);
			foreach (InvoiceLine detail in json.detail) {
				if ((detail.ProductId == 0 || detail.ProductId == null) 
						&& (detail.AccountId == 0 || detail.AccountId == null))
					continue;
				journal = Database.Get(new Journal() {
					DocumentId = (int)document.idDocument,
					JournalNum = lineNum
				});
				journal.DocumentId = (int)document.idDocument;
				journal.JournalNum = lineNum++;
				journal.AccountId = (int)detail.AccountId;
				journal.NameAddressId = document.DocumentNameAddressId;
				journal.Memo = detail.Memo;
				journal.Outstanding += sign * detail.LineAmount - journal.Amount;
				journal.Amount = sign * detail.LineAmount;
				Database.Update(journal);
				Line line = new Line();
				line.idLine = journal.idJournal;
				line.Qty = detail.Qty;
				line.ProductId = detail.ProductId;
				line.LineAmount = detail.LineAmount;
				line.VatCodeId = detail.VatCodeId;
				line.VatRate = detail.VatRate;
				line.VatAmount = detail.VatAmount;
				Database.Update(line);
			}
			Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum + ")");
			Database.Execute("DELETE FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum);
			if (vat != 0 || vatJournal.idJournal != null) {
				vat *= sign;
				decimal changeInVatAmount = vat - vatJournal.Amount;
				Utils.Check(document.VatPaid == null || changeInVatAmount == 0, "Cannot alter VAT on this document, it has already been declared");
				vatJournal.DocumentId = (int)document.idDocument;
				vatJournal.AccountId = (int)Acct.VATControl;
				vatJournal.NameAddressId = document.DocumentNameAddressId;
				vatJournal.Memo = "Total VAT";
				vatJournal.JournalNum = lineNum++;
				vatJournal.Amount = vat;
				vatJournal.Outstanding += changeInVatAmount;
				Database.Update(vatJournal);
			}
			JObject newDoc = getCompleteDocument(document.idDocument);
			Database.AuditUpdate("Document", document.idDocument, oldDoc, newDoc);
			Settings.RegisterNumber(this, document.DocumentTypeId, Utils.ExtractNumber(document.DocumentIdentifier));
			Database.Commit();
			return new AjaxReturn() { message = "Document saved", id = document.idDocument };
		}

		public AjaxReturn DocumentDelete(int id) {
			return deleteDocument(id, InvoiceDoc, CreditDoc);
		}

		/// <summary>
		/// Retrieve a payment for editing
		/// </summary>
		public void Payment(int id) {
			PaymentDocument document = getPayment(id);
			JObject record = document.ToJObject();
			Database.NextPreviousDocument(record, "WHERE DocumentTypeId = " + (int)PaymentDoc);
			record.Add("BankAccounts", SelectBankAccounts());
			record.Add("Names", SelectNames(NameType));
			Record = record;
		}

		/// <summary>
		/// Retrieve a payment, or prepare a new one
		/// </summary>
		PaymentDocument getPayment(int? id) {
			PaymentHeader header = getDocument<PaymentHeader>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)PaymentDoc;
				header.DocumentDate = Utils.Today;
				header.DocumentName = "";
				header.DocumentIdentifier = "Payment";
				if(Settings.DefaultBankAccount != null)
					header.DocumentAccountId = (int)Settings.DefaultBankAccount;
				if (GetParameters["name"].IsInteger()) {
					JObject name = Database.QueryOne("*", "WHERE idNameAddress = " + GetParameters["name"], "NameAddress");
					if (name != null) {
						checkNameType(name.AsString("Type"), NameType);
						header.DocumentNameAddressId = name.AsInt("idNameAddress");
						header.DocumentAddress = name.AsString("Address");
						header.DocumentName = name.AsString("Name");
					}
				}
			} else {
				checkDocType(header.DocumentTypeId, PaymentDoc);
				checkNameType(header.DocumentNameAddressId, NameType);
			}
			PaymentDocument previous = new PaymentDocument();
			previous.header = header;
			previous.detail = PaymentListing(header.idDocument, header.DocumentNameAddressId).ToList();
			return previous;
		}

		/// <summary>
		/// Get the payment details of an existing payment from the audit trail.
		/// Finds the most recent audit record.
		/// </summary>
		public PaymentDocument PaymentGetAudit(int? id) {
			if (id > 0) {
				AuditTrail t = Database.QueryOne<AuditTrail>("SELECT * FROM AuditTrail WHERE TableName = 'Payment' AND ChangeType <= "
					+ (int)AuditType.Update + " AND RecordId = " + id + " ORDER BY DateChanged DESC");
				if(!string.IsNullOrEmpty(t.Record))
					return JObject.Parse(t.Record).ToObject<PaymentDocument>();
			}
			return null;
		}

		/// <summary>
		/// List all the documents with an outstanding amount.
		/// For an existing document, also include all other documents that were paid by this one.
		/// </summary>
		public IEnumerable<PaymentLine> PaymentListing(int? id, int? name) {
			if (name > 0) {
				if (id == null)
					id = 0;
				return Database.Query<PaymentLine>("SELECT Document.*, DocType, "
						+ (LedgerAccount == Acct.PurchaseLedger ?
						"-Amount AS Amount, CASE WHEN PaymentAmount IS NULL THEN -Outstanding ELSE PaymentAmount - Outstanding END AS Outstanding" :
						"Amount, CASE WHEN PaymentAmount IS NULL THEN Outstanding ELSE PaymentAmount + Outstanding END AS Outstanding")
						+ @", CASE WHEN PaymentAmount IS NULL THEN 0 ELSE PaymentAmount END AS AmountPaid
FROM Document
JOIN Journal ON DocumentId = idDocument AND AccountId = " + (int)LedgerAccount + @"
JOIN DocumentType ON idDocumentType = DocumentTypeId
LEFT JOIN Payments ON idPaid = idDocument AND idPayment = " + id + @"
WHERE NameAddressId = " + name + @"
AND (Outstanding <> 0 OR PaymentAmount IS NOT NULL)
ORDER BY DocumentDate");
			}
			return new List<PaymentLine>();
		}

		public AjaxReturn PaymentPost(PaymentDocument json) {
			decimal amount = 0;
			Database.BeginTransaction();
			PaymentHeader document = json.header;
			checkDocType(document.DocumentTypeId, PaymentDoc);
			checkNameType(document.DocumentNameAddressId, NameType);
			checkAccountIsAcctType(document.DocumentAccountId, AcctType.Bank, AcctType.CreditCard);
			if (document.idDocument == null)
				allocateDocumentIdentifier(document);
			PaymentDocument oldDoc = getPayment(document.idDocument);
			int sign = -SignFor(PaymentDoc);
			// Update the outstanding on the paid documents
			foreach (PaymentLine payment in json.detail) {
				decimal a = payment.AmountPaid;
				PaymentLine old = oldDoc.PaymentFor(payment.idDocument);
				if (old != null)
					a -= old.AmountPaid;	// reduce update by the amount paid last time it was saved
				int? docId = payment.idDocument;
				if (a != 0) {
					Database.Execute("UPDATE Journal SET Outstanding = Outstanding - " + sign * a
						+ " WHERE DocumentId = " + Database.Quote(docId) + " AND AccountId = " + (int)LedgerAccount);
					amount += a;
				}
			}
			json.detail = json.detail.Where(l => l.AmountPaid != 0).ToList();
			decimal changeInDocumentAmount;
			decimal changeInOutstanding;
			// Virtual method, as calculation is different for customers and suppliers
			calculatePaymentChanges(json, amount, out changeInDocumentAmount, out changeInOutstanding);
			document.DocumentTypeId = (int)PaymentDoc;
			Database.Update(document);
			// Now delete the old cross reference records, and replace with new
			Database.Execute("DELETE FROM Payments WHERE idPayment = " + document.idDocument);
			foreach (PaymentLine payment in json.detail) {
				if (payment.AmountPaid != 0) {
					Database.Execute("INSERT INTO Payments (idPayment, idPaid, PaymentAmount) VALUES("
						+ document.idDocument + ", " + payment.idDocument + ", " + payment.AmountPaid + ")");
				}
			}
			// Journal between bank account and sales/purchase ledger
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)document.Id,
				JournalNum = 1
			});
			journal.DocumentId = (int)document.idDocument;
			journal.JournalNum = 1;
			journal.NameAddressId = document.DocumentNameAddressId;
			journal.Memo = document.DocumentMemo;
			journal.AccountId = document.DocumentAccountId;
			journal.Amount += changeInDocumentAmount;
			journal.Outstanding += changeInOutstanding;
			Database.Update(journal);
			journal = Database.Get(new Journal() {
				DocumentId = (int)document.Id,
				JournalNum = 2
			});
			journal.DocumentId = (int)document.idDocument;
			journal.JournalNum = 2;
			journal.NameAddressId = document.DocumentNameAddressId;
			journal.Memo = document.DocumentMemo;
			journal.AccountId = (int)LedgerAccount;
			journal.Amount -= changeInDocumentAmount;
			journal.Outstanding -= changeInOutstanding;
			Database.Update(journal);
			Line line = Database.Get(new Line() { idLine = journal.idJournal });
			line.idLine = journal.idJournal;
			line.LineAmount += PaymentDoc == DocType.BillPayment ? -changeInDocumentAmount : changeInDocumentAmount;
			Database.Update(line);
			oldDoc = PaymentGetAudit(document.idDocument);
			Database.AuditUpdate("Payment", document.idDocument, oldDoc == null ? null : oldDoc.ToJObject(), json.ToJObject());
			Database.Commit();
			return new AjaxReturn() { message = "Payment saved", id = document.idDocument };
		}

		protected abstract void calculatePaymentChanges(PaymentDocument json, decimal amount, out decimal changeInDocumentAmount, out decimal changeInOutstanding);

		public AjaxReturn PaymentDelete(int id) {
			return deleteDocument(id, PaymentDoc);
		}

		/// <summary>
		/// Show payment history for a document (either a payment or an invoice/credit)
		/// </summary>
		public void PaymentHistory(int id) {
			Extended_Document document = getDocument<Extended_Document>(id);
			bool payment;
			Utils.Check(document.DocumentTypeId != 0, "Document {0} not found", id);
			switch ((DocType)document.DocumentTypeId) {
				case DocType.Invoice:
				case DocType.CreditMemo:
				case DocType.Bill:
				case DocType.Credit:
					payment = false;
					break;
				case DocType.Payment:
				case DocType.BillPayment:
					payment = true;
					break;
				default:
					throw new CheckException("No Payment History for {0}s", ((DocType)document.DocumentTypeId).UnCamel());
			}
			Record = new JObject().AddRange(
				"header", document,
				"detail", Database.Query(@"SELECT * FROM Payments
JOIN Extended_Document ON idDocument = " + (payment ? "idPaid" : "idPayment") + @"
WHERE " + (payment ? "idPayment" : "idPaid") + " = " + id + @"
ORDER BY DocumentDate, idDocument"));
		}

		public void VatCodes() {
			// Use customer template
			Module = "customer";
		}

		public object VatCodesListing() {
			return Database.Query("*", "ORDER BY Code", "VatCode");
		}

		public void VatCode(int id) {
			// Use customer template
			Module = "customer";
			VatCode record = Database.Get<VatCode>(id);
			if (record.Id != null)
				Title += " - " + record.Code + ":" + record.VatDescription;
			Record = record;
		}

		public AjaxReturn VatCodeDelete(int id) {
			AjaxReturn result = new AjaxReturn();
			try {
				Database.Delete("VatCode", id, true);
				result.message = "VAT code deleted";
			} catch {
				result.error = "Cannot delete - VAT code in use";
			}
			return result;
		}

		public AjaxReturn VatCodePost(VatCode json) {
			return PostRecord(json, true);
		}

		void addNameToMenuOptions(int id) {
			foreach (MenuOption option in Menu)
				if(option.Text.StartsWith("New "))
					option.Url += "&name=" + id;
		}

		public class RecordDetail : NameAddress {
			public decimal? Outstanding;
		}

		public class InvoiceDocument : JsonObject {
			public Extended_Document header;
			public List<InvoiceLine> detail;
		}

		public class PaymentLine : Document {
			public string DocType;
			public decimal Amount;
			public decimal Outstanding;
			public decimal AmountPaid;
		}

		public class PaymentHeader : Extended_Document {
			public decimal Allocated;
			public decimal Remaining;
		}

		public class PaymentDocument : JsonObject {

			public PaymentDocument() {
				detail = new List<PaymentLine>();
			}

			public PaymentHeader header;
			public List<PaymentLine> detail;

			public PaymentLine PaymentFor(int? documentId) {
				return detail.FirstOrDefault(d => d.idDocument == documentId);
			}
		}

	}

	public class InvoiceLine : Line {
		public int? AccountId;
		public string Memo;
	}

}

