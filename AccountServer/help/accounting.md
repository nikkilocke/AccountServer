# Accounting

## List Accounts

This lists all the general ledger accounts. These are used to analyse your transaction lines into categories. Click on an account to see the details.

Click *New Account* to create a new one. Each account must have a unique name, and must be assigned to a Type. The account types available are shown in the order in which they appear in the Profit and Loss account.

## Account Detail

This shows all the postings to the account (most recent first). You can click on a posting to be taken to the relevant document.

## Journals

A journal moves money between general ledger accounts. They are usually used to reconcile the day-to-day accounts as input with your accountant's view of the accounts. All journals must add up to zero (i.e. the total of credits and debits must be the same) so the accounts still balance.

For example, if you pay a phone bill every 3 months, and a bill partly covers one financial year, and partly another, your accountant will want to show each part of the bill in the correct financial year. 

Another example is where you are company with fixed assets - normal accounting rules allow you to spread the cost of a fixed asset over the lifetime of the asset. 

Journals are extremely powerful, and can move money between any accounts at all, including important control accounts like VAT Control. You should only enter journals if you have sufficient knowledge of accounting, or have been advised what to do by your accountant.

Examples of typical types of journals appear below.

### Prepayments

A prepayment is where you have paid something in one financial year all or part of which relates to the following year.

For example, your phone bill may include a line rental amount which is paid every 3 months on advance. If 2 of those months are in the next financial year, you will need to create Prepayment journals. 

To create prepayments, you first need to set up one or more prepayments accounts, whose *Type* should be *Current Asset*.

Then create a journal, usually dated on the last day of the year, which transfers 2 month's rental out of the expense account concerned (e.g. Telephone), and into a Prepayments account. The journal will Credit Telephone and Debit Prepayments. On the first day of the next year, create a reversing journal, which moves the money back again, by Debiting Telephone and Crediting Prepayments.

This way, the Profit and Loss for each year will only show the expense that relates to that year, and your Balance sheet will reflect the amount you have paid in advance as a Current Asset.

### Accruals

An accrual is where you have not been billed by the end of the year for some expense you have incurred this year.

For example, your phone bill may include a call cost amount, which is paid every 3 months in arrears. Unless the phone bill arrives on the last day of your financial year, there will be some amount of call costs that have not been billed, but which you should account for in this year. 

To create accruals, you first need to set up one or more accruals accounts, whose *Type* should be *Current Liability*.

Then create a journal, usually dated on the last day of the year, which transfers the estimated rental between an Accruals account and the expense account concerned (e.g. Telephone). The journal will Debit Telephone and Credit Accruals. On the first day of the next year, create a reversing journal, which moves the money back again, by Crediting Telephone and Debiting Accruals.

This way, the Profit and Loss for each year will only show the expense that relates to that year, and your Balance sheet will reflect the amount you owe as a Current Liability.

### Fixed Assets and Depreciation

These are dealt with by setting up *Fixed Assets* accounts for the cost and depreciation of fixed assets, and setting up a corresponding *Expense* account for the depreciation. When you buy an asset, if you have posted the cost to an expense account, you create a journal to move the expense into the fixed assets account (Credit the expense account, and Debit the Current Assets cost account). This way, the expense of buying the asset will not appear in your Profit and Loss account, but on your Balance Sheet.

At the end of each year, you create a journal to move the amount of depreciation for that year from the Fixed Assets depreciation account into the expense depreciation account. The journal will Credit the Current Assets depreciation account, and Debit the Expense depreciation account. This way the cost of the depreciation will appear in your Profit and Loss account, and reduce the value of Current Assets on your Balance Sheet.

## List Journals

This shows all the journals posted to the system, most recent first. Click on one to edit it.

## Adding journals

Enter the journaL date, and a Memo describing the purpose of the journal.

For each line of the journal, select the account and the amount to credit or debit that account. You can also fill in a memo describing that line. The amount needed to balance the journal will be shown at the top.

As you enter each line, a new line will be created, with a Credit or Debit amount to make the journal balance (but no account, until you fill it in). You can delete an existing line by pressing the red ![x](../images/close.png). 

## Names

*Names* lets you edit the list of names you have used in journals or banking transactions (other than customers, suppliers or members). Click on a name to edit it. This list is used to produce the drop-down list of names like the one you type when choosing a name for a journal. If you have a lot of names which appear on that list, but are no longer useful, you can edit them, and check the hidden box to prevent them appearing on the list in future. Hidden names don't appear on the Names list by default, but you can reveal them again by clicking the *Show all* button (perhaps to edit one, and unhide it).

## VAT Return

Clicking on *VAT Return* will check all your VATable transactions which haven't been included on a VAT Return yet, up to the end of the last VAT quarter, and produce a standard VAT Return for you to submit to the government. 

Note that the VAT quarters are determined by your declared year start date in *Admin* *Settings*.

Once you are happy that everything is corrent, choose the payment date and bank account, and click *Save and Print*. All the transactions included will be marked as VAT paid, the payment will be added to the chosen bank account, and you can print out the VAT return.

If you want to see a previous VAT Return, select it from the *Other VAT returns* dropdown at the bottom.

If you want to see the details of every transaction that goes up to make the various figures, print the *VAT Detail Report* under *Reports*.
