# Banking

## Listing

The Banking Listing lists all the bank and credit card accounts which aren't hidden by default - you can view the hidden ones as well if you click the *Show all* button.

## Bank Detail

The Bank Detail screen lists all the transactions on the account (with the newest at the top), and the balance and current balance. The current balance does not include transactions which are dated in the future. Click on a transaction to see the detail or edit it.

You can change the Name, description, etc. by clicking on the *Edit* button.

You can create a new account by clicking the *New Account* button. 

Each account must have a unique name. Choose the type (Bank or Credit Card). The remaining fields are optional - Statement Format tells AccountServer how statements copied from the bank web page are laid out, so you can easily reconcile AccountServer with the bank, but you can leave that until you are ready to actually reconcile transactions. Next Cheque and Deposit numbers (from your cheque and deposit books) enable AccountServer to fill these details automatically, if required.

## Names

*Names* lets you edit the list of people who you have written cheques to, or received payments from (other than customers or suppliers). Click on a name to edit it. This list is used to produce the drop-down list of names like the one you type when choosing who to pay. If you have a lot of names which appear on that list, but are no longer useful, you can edit them, and check the hidden box to prevent them appearing on the list in future. Hidden names don't appear on the Names list by default, but you can reveal them again by clicking the *Show all* button (perhaps to edit one, and unhide it).

## Adding Transactions

Click on *New cheque*, *New deposit* or *New transfer* to create a new transaction. 

**NOTE Payments from customers and to suppliers should not be entered here**, but in *Customer* *Add Payment* and *Supplier* *Add Bill Payment* respectively. 

### Cheques and deposits

Type the name of the payee or payer - as you type, a drop-down list of previous payees with similar names appears. As you type, a drop down of existing people with similar names appears, which you can click on to choose one. If you type a new name in, when you leave the field you will be asked if you want to create a new name. If you choose an existing name, the lines from the last cheque to or deposit from that name will be copied in, which you can amend or delete as appropriate.

Now add the invoice lines - for each line, choose an account. Enter the Vat Code and Amount. Note that you can overwrite the calculated VAT if the other party has calculated the VAT differently (perhaps they use a different rounding method). You can add a note to the line in the Memo field if you like. As you enter each new line, a new blank line will appear below. You can delete an existing line by pressing the red ![x](../../images/close.png).

Note that the total and VAT total are updated automatically as you amend the lines.

### Transfers

Transfers are used to transfer money between accounts - e.g. between a current and deposit account, or to a stockbroker account. Just choose the account from the drop-down list, and enter the date and amount.

## Importing statements

You can import bank statements from your bank, to cut down on data entry. If the bank provides statements in QIF format, you can import them by clicking the *Choose file* button and pressing Import.

Even if the bank only provides a web page with your statement data on it, you can copy that from the bank's web page into the *Paste statement data here* box, and press Import.

Obviously, every bank's web site is slightly different, so you have to tell AccountServer what the format is. You do this by entering information in the *Statement format* field. On successful import, the format is saved against each of your bank accounts, so it is remembered for next time.

You indicate the format with a list of field names in curly brackets, separated by whatever separator the bank uses - usually {Tab} or {Newline}. Usually the bank's web site presents the data as a table, which AccountServer sees as a {Tab} between each column, and a {Newline} at the end of each row.

The fields you can use are:

|Field|Notes|
|-----|-----|
|Date|The date of the transaction|
|Payment|The amount paid out (if the bank shows payments and deposits in separate columns)|
|Deposit|The amount paid in (if the bank shows payments and deposits in separate columns)|
|Amount|The amount paid out (if the bank shows payments and deposits in the same column, with a minus sign or a CR for credits)|
|Name|The person paid or paying in|
|Memo|Data to include in the Memo field of the transaction|
|Id|Document identifier (e.g. cheque number or deposit number)|
|Any|Indicates there is some data on the bank's web page that you are not interested in|
|Tab|Indicates a column divider|
|Newline|Indicates a row end|

You can also add *Optional:* in front of a field if the bank provides it for some transactions and not others.

I am hoping to provde example formats for a few different banks and credit card companies on our website - if you have any that work well, feel free to send them to me using the [contact form](http://www.trumphurst.com/contact.php) on my website.

### Statement Matching

Once you have clicked Import, the imported transactions will be shown at the top of the screen. Click on one to match it - a list of similar transactions already in the AccountServer system will be shown on the bottom of the screen. Ones shown in red are very close matches (exactly the same amount, and a similar date). 

If the highlighted transaction from the bank statement is the same as one already in the system, click the Same checkbox against the one in the system. The bank statement transaction will then disappear (because it has been matched to one already on the system).

If the highlighted transaction from the bank statement is similar to one already on the system (e.g. a monthly payment where last month's payment is on the system, but the bank one isn't yet), click the New checkbox against the one in the system. A new transaction will be created as a copy of the old one, with the correct new amount and date. Check the transaction, and click Save to add it to the system. The bank statement transaction will then disappear (because it has been matched to the new one on the system).

If the highlighted transaction from the bank statement doesn't match any of the ones already on the system, click New next to the bank statement transaction. A dropdown menu will appear so you can choose the type of transaction (e.g. Transfer, Deposit, Customer Payment, Cheque, Bill Payment), then the system will create a new transaction of the selected type for you to complete, with the data from the bank statement already filled in.

Once all the bank statement transactions have been matched, you are done.

## Reconciling

When you recieve an official bank statement (either by post, or printed from the bank website), you should reconcile it with the system using *Reconcile*. This involves inputting the ending balance from the statement (*N.B. Overdrafts and credit card balances should be input as a minus number*), and ticking off the transactions on the screen which are also on the statement.

As you tick off each transaction, the cleared balance and difference is updated automatically. For the statement to reconcile, the difference should be zero.

If you are unable to reconcile (perhaps because there are some transactions missong, or with the wrong value), you can press the *Leave* button to save your work so far, enter or edit the incorrect transactions, and return.

If the reconciliation balances, you can save it - this will mark all the ticked transactions as reconciled (*Clr* will sho an 'X' on the bank detail screen).

Once you have reconciled, you can print the reconciliation for your records with *Save and Print*.