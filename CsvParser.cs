using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using CodeFirstWebFramework;

namespace AccountServer {
	/// <summary>
	/// Interface for any file processor, for monitoring progess, and detecting the line number of errors
	/// </summary>
	public interface FileProcessor {
		/// <summary>
		/// Character number reached in the whole file
		/// </summary>
		int Character { get; }

		/// <summary>
		/// Line number reached
		/// </summary>
		int Line { get; }
	}

	/// <summary>
	/// Parse a Csv or Tab delimited file
	/// </summary>
	public class CsvParser : FileProcessor {
		/// <summary>
		/// The current field
		/// </summary>
		StringBuilder _field;
		/// <summary>
		/// List of fields in the current line
		/// </summary>
		List<string> _line;
		// States
		static State Start = new StartState();
		static TabState TabStart = new TabState();
		static State FieldData = new State();
		static State QuotedField = new QuotedFieldState();
		/// <summary>
		/// True if at end of file
		/// </summary>
		bool _eof;
		/// <summary>
		/// Line number reached
		/// </summary>
		int _lineno;
		TextReader _reader;
		/// <summary>
		/// The start to start each line at.
		/// </summary>
		State _startState = Start;

		/// <summary>
		/// Add a character to the current field
		/// </summary>
		void AddChar(int ch) {
			_field.Append((char)ch);
		}

		/// <summary>
		/// Add current field to the line, and reset
		/// </summary>
		void AddField() {
			_line.Add(_field.ToString());
			_field.Length = 0;
		}

		/// <summary>
		/// Read the next character
		/// </summary>
		int ReadChar() {
			for (; ; ) {
				int ch = _reader.Read();
				Character++;
				switch (ch) {
					case -1:
						_eof = true;
						return ch;
					case '\r':
						continue;
					case '\n':
						_lineno++;
						return ch;
					default:
						return ch;
				}
			}
		}

		/// <summary>
		/// State when processing a normal field
		/// </summary>
		class State {
			public virtual State process(CsvParser p) {
				for(;;) {
					int ch = p.ReadChar();
					switch(ch) {
						case '\n':
						case -1:
							// End of line/file - add current field
							p.AddField();
							return null;
						case '\t':
							// This must be a tab-delimited file - switch mode to tab delimited
							TabStart.AddField(p);
							p._startState = TabStart;
							return TabStart;
						case ',':
							// End of field
							p.AddField();
							return Start;	// Initial field state
						default:
							// Add character to field
							p.AddChar(ch);
							continue;
					}
				}
			}
		}
		/// <summary>
		/// Initial state when processing a field
		/// </summary>
		class StartState : State {
			public override State process(CsvParser p) {
				for(;;) {
					int ch = p.ReadChar();
					switch(ch) {
						case '\n':
						case -1:
							if (p._line.Count > 0) p.AddField();
							return null;
						case '\t':
							// This must be a tab-delimited file - switch mode to tab delimited
							TabStart.AddField(p);
							p._startState = TabStart;
							return TabStart;
						case ',':
							// Empty field
							p.AddField();
							continue;
						case '"':
							return QuotedField;
						default:
							// Anything else is a normal field - switch state to field processor
							p.AddChar(ch);
							return FieldData;
					}
				}
			}
		}
		/// <summary>
		/// Tab delimited file processor
		/// </summary>
		class TabState : State {
			public override State process(CsvParser p) {
				for (; ; ) {
					int ch = p.ReadChar();
					switch (ch) {
						case '\n':
						case -1:
							if (p._line.Count > 0 || p._field.Length > 0) AddField(p);
							return null;
						case '\t':
							AddField(p);
							continue;
						default:
							p.AddChar(ch);
							continue;
					}
				}
			}

			/// <summary>
			/// Switching to this state from Csv - remove any quotes from current field, and add it
			/// </summary>
			public void AddField(CsvParser p) {
				string f = p._field.ToString();
				if (f.StartsWith("\"") && f.EndsWith("\"")) {
					p._field.Length = 0;
					p._field.Append(f.Substring(1, f.Length - 2));
				}
				p.AddField();
			}
		}
		/// <summary>
		/// Special state for quoted fields (which may contain newlines)
		/// </summary>
		class QuotedFieldState : State {
			public override State process(CsvParser p) {
				for(;;) {
					int ch = p.ReadChar();
					switch(ch) {
						case -1:
							p.AddField();
							return null;
						case '"':
							ch = p.ReadChar();
							switch(ch) {
								case '\n':
								case -1:
									// Quote at end of line/file just terminates the field
									p.AddField();
									return null;
								case ',':
									// Quote, comma is just end of field
									p.AddField();
									return Start;
								case '\t':
									// Must be a tab-delimited file - switch
									p.AddField();
									return TabStart;
								case '"':
									// Quote Quote = Quote
									p.AddChar('"');
									continue;
								default:
									// Quote anychar is a syntax error, but we allow it and pass through unchanged
									p.AddChar('"');
									p.AddChar(ch);
									continue;
							}
						default:
							// This includes newlines and tabs!
							p.AddChar(ch);
							continue;
					}
				}
			}
		}
	   

		public CsvParser(TextReader r) {
			_reader = r;
			_lineno = 1;
			_field = new StringBuilder();
			Headers = ReadLine();
		}

		/// <summary>
		/// Set to true to turn off validation that there must be the same number of fields as headings
		/// </summary>
		public bool PermitAnyFieldCount;

		/// <summary>
		/// Read a record and return it as a JObject, using the heading line to name the fields
		/// </summary>
		public IEnumerable<JObject> Read() {
			while (!_eof) {
				string [] l = ReadLine();
				if(l.Length == 0) continue;
				Utils.Check(PermitAnyFieldCount || l.Length == Headers.Length, "Wrong number of fields in line {0} found:{1} expected {2}", Line, l.Length, Headers.Length);
				JObject data = new JObject();
				int max = Math.Min(l.Length, Headers.Length);
				for (int i = 0; i < max; i++) {
					data[Headers[i]] = l[i];
				}
				yield return data;
			}
		}

		/// <summary>
		/// The current record as an array of strings
		/// </summary>
		public string[] Data {
			get { return _line.ToArray(); }
		}

		/// <summary>
		/// The headers from the header line (i.e. the field names)
		/// </summary>
		public string[] Headers;

		/// <summary>
		/// For progress display
		/// </summary>
		public int Character { get; private set; }

		/// <summary>
		/// For error tracking
		/// </summary>
		public int Line { get; private set; }
	
		/// <summary>
		/// Read a record and return it as an array of strings
		/// </summary>
		public string [] ReadLine() {
			_field.Length = 0;
			_line = new List<string>();
			Line = _lineno;
			State state = _startState;
			do {
				state = state.process(this);
			} while(state != null);
			return _line.ToArray();
		}
	}
}
