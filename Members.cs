using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	public class Members : AppModule {

		public Members() {
			Menu = new MenuOption[] {
				new MenuOption("Listing", "/members/default.html"),
				new MenuOption("Subscription Payments", "/members/subscriptions.html"),
				new MenuOption("Membership Types", "/members/types.html"),
				new MenuOption("New Subscription Payments", "/members/document.html?id=0")
			};
		}

		public override void Default() {
			insertMenuOption(new MenuOption("New Member", "/members/detail.html?id=0"));
			insertMenuOption(new MenuOption("Year End", "/members/yearend.html"));
			DataTableForm form = new DataTableForm(this, typeof(Full_Member));
			Form = form;
			form.Options["select"] = "/members/detail.html";
			form.Remove("MemberTypeId");
			form.Remove("NameAddressId");
			form.Remove("Address");
			form.Remove("PostCode");
			form.Remove("Contact");
			form["Hidden"].Options["nonZero"] = new JObject().AddRange(
				"zeroText", "Current members only",
				"nonZeroText", "Include left members"
				);
			form.Show();
		}

		public object DefaultListing() {
			return Database.Query(@"SELECT * FROM Full_Member ORDER BY Name");
		}

		/// <summary>
		/// Get record for editing
		/// </summary>
		public void NameAddress(int id) {
			Member m = Database.QueryOne<Member>("SELECT * FROM Member WHERE NameAddressId = " + id);
			Utils.Check(m.idMember > 0, "Member not found");
			Method = "Detail";
			Detail((int)m.idMember);
		}

		/// <summary>
		/// Get record for editing
		/// </summary>
		public void Detail(int id) {
			Full_Member record = Database.Get<Full_Member>(id);
			if (record.Id == null) {
				JObject r = Database.QueryOne("SELECT MAX(MemberNo) AS MaxMemberNo FROM Member");
				record.MemberNo = r == null ? 1 : r.AsInt("MaxMemberNo") + 1;
			} else
				Title += " - " + record.Name;
			Form form = new CodeFirstWebFramework.Form(this, typeof(Full_Member), true);
			form.Remove("NameAddressId");
			form.Remove("MemberTypeName");
			SelectAttribute memberType = new SelectAttribute(SelectMemberTypes()) {
				Data = "MemberTypeId"
			};
			form.Replace(form.IndexOf("MemberTypeId"), memberType);
			Form = form;
			form.Data = record.ToJToken();
		}

		public AjaxReturn DetailPost(Full_Member json) {
			Utils.Check(json.MemberTypeId > 0, "Must choose membership type");
			Utils.Check(!string.IsNullOrWhiteSpace(json.Name), "Name must be filled in");
			Utils.Check(json.MemberNo > 0, "Must assign a membership number");
			Utils.Check(json.PaymentAmount >= 0, "Payment amount may not be negative");
			if (json.idMember == null && json.PaymentAmount == 0 && json.NumberOfPayments > 0)
				json.PaymentAmount = Math.Round(json.AnnualSubscription / json.NumberOfPayments);
			Database.BeginTransaction();
			// Record contains most of the data from the associated NameAddress record - save that first
			JObject nameAddress = json.ToJObject();
			if(json.NameAddressId > 0)
				nameAddress["idNameAddress"] = json.NameAddressId;
			nameAddress["Type"] = "M";
			Database.Update("NameAddress", nameAddress);
			json.NameAddressId = nameAddress.AsInt("idNameAddress");
			AjaxReturn r = PostRecord(json, true);
			if(r.error == null)
				Database.Commit();
			return r;
		}

		public void Subscriptions() {
			DataTableForm form = new DataTableForm(this, typeof(SubscriptionJournal));
			form.Options["select"] = "/members/document.html";
			form.Options["id"] = "idDocument";
			form.Options["table"] = "Document";
			Form = form;
			form.Show();
		}

		public object SubscriptionsListing() {
			return Database.Query("SELECT idDocument, DocumentAccountId, DocumentIdentifier, DocumentDate, -DocumentAmount AS DocumentAmount, DocumentMemo FROM Extended_Document WHERE DocumentTypeId = "
				+ (int)DocType.Subscriptions + " ORDER BY DocumentDate DESC");
		}

		JObject getSubscriptionJournal(Extended_Document header) {
			return new JObject().AddRange(
				"header", new SubscriptionJournal() {
					idDocument = header.idDocument,
					DocumentDate = header.DocumentDate,
					DocumentAccountId = header.DocumentAccountId,
					DocumentIdentifier = header.DocumentIdentifier,
					DocumentAmount = -header.DocumentAmount,
					DocumentMemo = header.DocumentMemo
				},
				"detail", Database.Query(@"SELECT Journal.NameAddressId, CONCAT(Name, ' (', " + Database.Cast("MemberNo", "CHAR") + @", ')') AS Member, -Amount AS Amount, Memo
FROM Journal
LEFT JOIN NameAddress ON idNameAddress = Journal.NameAddressId
LEFT JOIN Member ON Member.NameAddressId = Journal.NameAddressId
WHERE DocumentId = " + (header.idDocument ?? 0) + @"
AND JournalNum > 1
ORDER BY JournalNum")
				);
		}

		public void Document(int id) {
			Extended_Document header = Database.Get<Extended_Document>(id);
			if (header.idDocument == null) {
				header.DocumentTypeId = (int)DocType.Subscriptions;
				header.DocType = "Subscriptions";
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
				checkDocType(header.DocumentTypeId, DocType.Subscriptions);
			}
			JObject record = getSubscriptionJournal(header);
			HeaderDetailForm form = new HeaderDetailForm(this, typeof(SubscriptionJournal), typeof(SubscriptionPayment));
			SelectAttribute account = new SelectAttribute(SelectBankAccounts()) {
				Data = "DocumentAccountId"
			};
			form.Header.Options["table"] = "Document";
			form.Header.Options["canDelete"] = string.IsNullOrEmpty(header.Clr);
			// Following fields will be auto-generated as readonly - we want to edit them
			form.Header.Replace(form.Header.IndexOf("DocumentAccountId"), account);
			SelectAttribute member = new SelectAttribute(SelectMembers()) {
				Data = "Member",
				Type = "autoComplete"
			};
			member.Options["mustExist"] = true;
			form.Detail.Replace(form.Detail.IndexOf("Member"), member);
			Form = form;
			Record = record;
		}

		public AjaxReturn DocumentPost(SubscriptionDocument json) {
			AjaxReturn result = new AjaxReturn();
			Database.BeginTransaction();
			JObject oldDoc = null;
			if (json.header.idDocument > 0) {
				oldDoc = getCompleteDocument(json.header.idDocument);
				checkDocType(((JObject)oldDoc["header"]).AsInt("DocumentTypeId"), DocType.Subscriptions);
			}
			Utils.Check(json.header.DocumentAccountId > 0, "Account not supplied");
			decimal total = 0;
			foreach (SubscriptionPayment detail in json.detail) {
				if (detail.NameAddressId > 0 && detail.Amount != 0)
					total += detail.Amount;
			}
			Utils.Check(json.header.DocumentAmount == total, "Document does not balance");
			var lineNum = 1;
			Document document = new Document() {
				idDocument = json.header.idDocument,
				DocumentTypeId = (int)DocType.Subscriptions,
				DocumentDate = json.header.DocumentDate,
				DocumentIdentifier = json.header.DocumentIdentifier,
				DocumentMemo = json.header.DocumentMemo
			};
			Database.Update(document);
			result.id = document.idDocument;
			Journal journal = Database.Get(new Journal() {
				DocumentId = (int)document.idDocument,
				JournalNum = lineNum
			});
			journal.DocumentId = (int)document.idDocument;
			journal.JournalNum = lineNum++;
			journal.AccountId = json.header.DocumentAccountId;
			journal.NameAddressId = 1;
			journal.Memo = json.header.DocumentMemo;
			journal.Amount = json.header.DocumentAmount;
			journal.Outstanding = json.header.DocumentAmount;
			Database.Update(journal);
			foreach (SubscriptionPayment detail in json.detail) {
				if (detail.NameAddressId > 0 && detail.Amount != 0) {
					journal = Database.Get(new Journal() {
						DocumentId = (int)document.idDocument,
						JournalNum = lineNum
					});
					if (lineNum > 1) {
						if (journal.idJournal > 0) {
							// Existing payment
							if (journal.NameAddressId != detail.NameAddressId) {
								// Changed member
								Database.Execute("UPDATE Member SET AmountDue = AmountDue - " + journal.Amount + " WHERE NameAddressId = " + journal.NameAddressId);
								Database.Execute("UPDATE Member SET AmountDue = AmountDue + " + journal.Amount + " WHERE NameAddressId = " + detail.NameAddressId);
							} else if (journal.Amount != detail.Amount) {
								// Just changed amount
								Database.Execute("UPDATE Member SET AmountDue = AmountDue + " + (detail.Amount - journal.Amount) + " WHERE NameAddressId = " + detail.NameAddressId);
							}
						} else {
							Database.Execute("UPDATE Member SET AmountDue = AmountDue + " + detail.Amount + " WHERE NameAddressId = " + detail.NameAddressId);
						}
					}
					journal.DocumentId = (int)document.idDocument;
					journal.JournalNum = lineNum;
					journal.AccountId = (int)Acct.SubscriptionsIncome;
					checkNameType(detail.NameAddressId, "M");
					journal.NameAddressId = detail.NameAddressId;
					journal.Memo = detail.Memo;
					journal.Outstanding = -detail.Amount;
					journal.Amount = -detail.Amount;
					Database.Update(journal);
					if (lineNum > 1) {
						// Create a dummy line record
						Line line = new Line() {
							idLine = journal.idJournal,
							Qty = 0,
							LineAmount = detail.Amount,
							VatCodeId = null,
							VatRate = 0,
							VatAmount = 0
						};
						Database.Update(line);
					}
					lineNum++;
				}
			}
			foreach(Journal j in Database.Query<Journal>("SELECT * FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum)) {
				Database.Execute("UPDATE Member SET AmountDue = AmountDue - " + j.Amount + " WHERE NameAddressId = " + j.NameAddressId);
			}
			Database.Execute("DELETE FROM Line WHERE idLine IN (SELECT idJournal FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum + ")");
			Database.Execute("DELETE FROM Journal WHERE DocumentId = " + document.idDocument + " AND JournalNum >= " + lineNum);
			JObject newDoc = getCompleteDocument(document.idDocument);
			Database.AuditUpdate("Document", document.idDocument, oldDoc, newDoc);
			Database.Commit();
			result.message = "Subscription Payments saved";
			return result;
		}

		public AjaxReturn DocumentDelete(SubscriptionJournal json) {
			Utils.Check(json.idDocument > 0, "No document to delete");
			Database.BeginTransaction();
			foreach (Journal j in Database.Query<Journal>("SELECT * FROM Journal WHERE DocumentId = " + json.idDocument + " AND JournalNum > 1")) {
				Database.Execute("UPDATE Member SET AmountDue = AmountDue - " + j.Amount + " WHERE NameAddressId = " + j.NameAddressId);
			}
			AjaxReturn r = deleteDocument((int)json.idDocument, DocType.Subscriptions);
			if (r.error == null) {
				Database.Commit();
				r.redirect = "/members/subscriptions.html";
			}
			return r;
		}

		public void Types() {
			insertMenuOption(new MenuOption("New Membership Type", "/members/type.html?id=0&from=%2Fmembers%2Ftypes.html"));
			DataTableForm form = new DataTableForm(this, typeof(MemberType));
			Form = form;
			form.Options["select"] = "/members/type.html";
			form.Show();
		}

		public object TypesListing() {
			return Database.Query(@"SELECT * FROM MemberType ORDER BY MemberTypeName");
		}

		public void Type(int id) {
			MemberType record = Database.Get<MemberType>(id);
			Form form = new Form(this, typeof(MemberType));
			if (record.Id != null)
				Title += " - " + record.MemberTypeName;
			form.Data = record.ToJToken();
			Form = form;
			form.Show();
		}

		public AjaxReturn TypePost(MemberType json) {
			if (json.NumberOfPayments < 1)
				json.NumberOfPayments = 1;
			return PostRecord(json);
		}

		public void YearEnd() {
			if (Request.HttpMethod == "POST") {
				bool clear = PostParameters.AsInt("clear") > 0;
				Database.BeginTransaction();
				foreach (Full_Member m in Database.Query<Full_Member>(@"SELECT * FROM Full_Member WHERE Hidden <> 1").ToList()) {
					m.AmountDue = m.AnnualSubscription + (clear ? 0 : m.AmountDue);
					m.PaymentAmount = Math.Round(m.AmountDue / (m.NumberOfPayments < 1 ? 1 : m.NumberOfPayments), 2);
					Database.Update(m, true);
				}
				Database.Commit();
				Redirect("/members");
			}
		}

		public JObjectEnumerable SelectMembers() {
			return Database.Query(@"SELECT NameAddressId AS id, CONCAT(Name, ' (', " + Database.Cast("MemberNo", "CHAR") + @", ')') AS value, CASE WHEN PaymentAmount < AmountDue THEN PaymentAmount ELSE AmountDue END AS PaymentAmount
FROM Member
JOIN NameAddress ON idNameAddress = NameAddressId
WHERE Hidden = 0
ORDER BY Name");
		}

		[Writeable]
		public class SubscriptionPayment : JsonObject {
			[Field(Visible = false)]
			public int NameAddressId;
			public string Member;
			[Length(75)]
			public string Memo;
			public decimal Amount;
		}

		[Writeable]
		public class SubscriptionJournal : JsonObject {
			[Primary]
			public int? idDocument;
			public DateTime DocumentDate;
			public string DocumentIdentifier;
			[ForeignKey("Account")]
			public int DocumentAccountId;
			[ReadOnly]
			public decimal DocumentAmount;
			[Length(0)]
			public string DocumentMemo;
		}

		public class SubscriptionDocument : JsonObject {
			public SubscriptionJournal header;
			public List<SubscriptionPayment> detail;
		}

	}
}
