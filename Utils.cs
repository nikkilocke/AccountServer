using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccountServer {
	public static class Utils {
		/// <summary>
		/// For converting between json string and JObject
		/// </summary>
		static JsonSerializer _converter;

		static Utils() {
			_converter = new JsonSerializer();
			_converter.Converters.Add(new DecimalFormatJsonConverter());
		}

		/// <summary>
		/// Add a NameValue collection to a JObject. Chainable.
		/// </summary>
		public static JObject AddRange(this JObject self, NameValueCollection c) {
			foreach (string key in c.Keys)
				self[key] = c[key];
			return self;
		}

		/// <summary>
		/// Add the properties of one JObject to another. Chainable.
		/// </summary>
		public static JObject AddRange(this JObject self, JObject c) {
			foreach (JProperty p in c.Properties())
				self[p.Name] = p.Value;
			return self;
		}

		/// <summary>
		/// Helper to add a list of stuff to a JObject. Chainable.
		/// </summary>
		/// <param name="content">Of the form: string, object - string = value
		/// or JObject - adds properties of JObject
		/// or NameValueCollection - adds collection members</param>
		public static JObject AddRange(this JObject self, params object[] content) {
			string key = null;
			foreach (object a in content) {
				if (key == null) {
					Type t = a.GetType();
					if (t == typeof(string)) {
						key = (string)a;
					} else if (t == typeof(JObject)) {
						AddRange(self, (JObject)a);
					} else if (typeof(NameValueCollection).IsAssignableFrom(t)) {
						AddRange(self, (NameValueCollection)a);
					} else {
						throw new CheckException("JObject.Add:{0}={1} as key", t.Name, a);
					}
				} else {
					self[key] = a.ToJToken();
					key = null;
				}
			}
			if (key != null)
				throw new CheckException("JObject.Add:No value supplied for key {0}", key);
			return self;
		}

		/// <summary>
		/// Return this string, with first letter upper case
		/// </summary>
		public static string Capitalise(this string s) {
			return string.IsNullOrEmpty(s) ? s : s.Substring(0, 1).ToUpper() + s.Substring(1);
		}

		/// <summary>
		/// Convert C# object to JToken
		/// </summary>
		public static JToken ToJToken(this object o) {
			return o == null ? null : JToken.FromObject(o);
		}

		/// <summary>
		/// this[name] as a bool
		/// </summary>
		public static bool AsBool(this JObject self, string name) {
			if (self == null)
				return false;
			JToken val = self[name];
			if(val == null || val.Type == JTokenType.Null)
				return false;
			string v = val.To<string>().Trim().ToLower();
			switch (v) {
				case "false":
				case "0":
				case "":
				case "null":
				case "undefined":
					return false;
				default:
					return true;
			}
		}

		/// <summary>
		/// this[name] as a DateTime
		/// </summary>
		public static DateTime AsDate(this JObject self, string name) {
			return self[name].To<DateTime>();
		}

		/// <summary>
		/// this[name] as a decimal
		/// </summary>
		public static decimal AsDecimal(this JObject self, string name) {
			if (self == null)
				return 0;
			JToken val = self[name];
			return val == null || string.IsNullOrEmpty(val.To<string>()) ? 0 : val.ToObject<decimal>();
		}

		/// <summary>
		/// this[name] as a double
		/// </summary>
		public static double AsDouble(this JObject self, string name) {
			if (self == null)
				return 0;
			JToken val = self[name];
			return val == null || string.IsNullOrEmpty(val.To<string>()) ? 0 : val.ToObject<double>();
		}

		/// <summary>
		/// this[name] as an int
		/// </summary>
		public static int AsInt(this JObject self, string name) {
			if (self == null)
				return 0;
			JToken val = self[name];
			return val == null || string.IsNullOrEmpty(val.To<string>()) ? 0 : val.ToObject<int>();
		}

		/// <summary>
		/// this[name] as a JObject
		/// </summary>
		public static JObject AsJObject(this JObject self, string name) {
			if (self == null)
				return null;
			JToken val = self[name];
			return (JObject)val;
		}

		/// <summary>
		/// this[name] as a string
		/// </summary>
		public static string AsString(this JObject self, string name) {
			if (self == null)
				return null;
			JToken val = self[name];
			return val == null ? null : val.ToObject<string>();
		}

		public static T As<T>(this JObject self, string name) where T:class {
			if (self == null)
				return null;
			JToken val = self[name];
			return val == null ? null : val.ToObject<T>();
		}

		/// <summary>
		/// Convert this JObject to a C# object of type T
		/// </summary>
		public static T To<T>(this JToken self) {
			try {
				return self.ToObject<T>(_converter);
			} catch (Exception ex) {
				Match m = Regex.Match(ex.Message, "Error converting value (.*) to type '(.*)'. Path '(.*)', line");
				if (m.Success)
					throw new CheckException(ex, "{0} is an invalid value for {1}", m.Groups[1], m.Groups[3]);
				throw new CheckException(ex, "Could not convert {0} to {1}", self, typeof(T).Name);
			}

		}

		/// <summary>
		/// Assert condition is true, throw a CheckException if not.
		/// </summary>
		public static void Check(bool condition, string error) {
			if (!condition) 
				throw new CheckException(error);
		}

		/// <summary>
		/// Assert condition is true, throw a CheckException if not.
		/// </summary>
		public static void Check(bool condition, string format, params object[] args) {
			if (!condition) 
				throw new CheckException(format, args);
		}

		/// <summary>
		/// Regex to match decimals
		/// </summary>
		public static Regex DecimalRegex = new Regex(@"^[+-]?\d+(:?\.\d*)?$", RegexOptions.Compiled);

		/// <summary>
		/// If s is a positive integer, return it, otherwise 0
		/// </summary>
		public static int ExtractNumber(string s) {
			if (string.IsNullOrEmpty(s))
				return 0;
			Match m = Utils.InvoiceNumber.Match(s);
			return m.Success ? int.Parse(m.Value) : 0;
		}

		/// <summary>
		/// Convert Url to a local file reference, and return a FileInfo for the file.
		/// Throws if the file is not in WebFolder, or a subfolder thereof.
		/// </summary>
		public static FileInfo FileInfoForUrl(string url) {
			if (url.StartsWith("/"))
				url = url.Substring(1);
			FileInfo f = new FileInfo(Path.Combine(AppSettings.Default.WebFolder, url));
			Utils.Check(f.FullName.StartsWith(new FileInfo(AppSettings.Default.WebFolder).FullName), "Illegal file access");
			return f;
		}

		/// <summary>
		/// Regex matches integers
		/// </summary>
		public static Regex IntegerRegex = new Regex(@"^[+-]?\d+$", RegexOptions.Compiled);

		/// <summary>
		/// Regex matches positive integers
		/// </summary>
		public static Regex InvoiceNumber = new Regex(@"^\d+$", RegexOptions.Compiled);

		/// <summary>
		/// True if all properties of this are null (or if this itself is null)
		/// </summary>
		public static bool IsAllNull(this JObject j) {
			return j == null || j.PropertyValues().Where(v => v.Type != JTokenType.Null).FirstOrDefault() == null;
		}

		public static bool IsDecimal(this string s) {
			return s == null ? false : DecimalRegex.IsMatch(s);
		}

		public static bool IsInteger(this string s) {
			return s == null ? false : IntegerRegex.IsMatch(s);
		}

		/// <summary>
		/// Convert this json string to a C# object of type t.
		/// </summary>
		public static object JsonTo(this string s, Type t) {
			return JsonConvert.DeserializeObject(s, t, new DecimalFormatJsonConverter());
		}

		/// <summary>
		/// Convert this json string to a C# object of type T.
		/// </summary>
		public static T JsonTo<T>(this string s) {
			return JsonConvert.DeserializeObject<T>(s, new DecimalFormatJsonConverter());
		}

		/// <summary>
		/// Translate NameType letter to human-readable form
		/// </summary>
		public static string NameType(this string type) {
			switch (type) {
				case "C":
					return "Customer";
				case "S":
					return "Supplier";
				case "O":
					return "Other Name";
				case "":
				case null:
					return "Unknown";
				default:
					return "Type " + type;
			}
		}

		/// <summary>
		/// Split text at the first supplied delimiter.
		/// Return the text before the delimiter, and set text to the remainder.
		/// </summary>
		public static string NextToken(ref string text, params string[] delimiters) {
			string[] parts = text.Split(delimiters, 2, StringSplitOptions.None);
			text = parts.Length > 1 ? parts[1] : "";
			return parts[0];
		}

		/// <summary>
		/// Time Zone to use throughout
		/// </summary>
		public static TimeZoneInfo _tz = TimeZoneInfo.Local;

		/// <summary>
		/// For testing - set this to an offset, and all dates & times will be offset by this amount.
		/// Enables a test to be run as if the computer time clock was offset by this amount - 
		/// i.e. the date & time were set exactly the same as when the test was first run.
		/// </summary>
		internal static TimeSpan _timeOffset = new TimeSpan(0);

		public static DateTime Now {
			get {
				return TimeZoneInfo.ConvertTime(DateTime.UtcNow + _timeOffset, _tz);
			}
		}

		public static DateTime Today {
			get {
				return Now.Date;
			}
		}

		/// <summary>
		/// If s starts and ends with a double-quote ("), remove them
		/// </summary>
		public static string RemoveQuotes(string s) {
			if (s.StartsWith("\"") && s.EndsWith("\""))
				s = s.Substring(1, s.Length - 2);
			return s;
		}

		/// <summary>
		/// Compare 2 strings, and return a number between 0 & 1 indicating what proportion of the words were the same.
		/// </summary>
		public static float SimilarTo(this string self, string s) {
			if (string.IsNullOrWhiteSpace(self) || string.IsNullOrWhiteSpace(s))
				return 0;
			s = s.ToUpper();
			self = self.ToUpper();
			int tot = 0;
			int matched = 0;
			foreach (Match m in Regex.Matches(self, @"\w+")) {
				tot += m.Length;
				if (s.Contains(m.Value))
					matched += m.Length;
			}
			return tot == 0 ? 0 : (float)matched / tot;
		}

		/// <summary>
		/// Convert a C# object to json
		/// </summary>
		public static string ToJson(this object o) {
			return JsonConvert.SerializeObject(o, new DecimalFormatJsonConverter());
		}

		/// <summary>
		/// Convert a CamelCase variable name to human-readable form - e.g. "Camel  Case".
		/// Accepts an object, so you can use it directly on Enum values.
		/// </summary>
		public static string UnCamel(this object s) {
			return UnCamel(s.ToString());
		}

		/// <summary>
		/// Convert a CamelCase variable name to human-readable form - e.g. "Camel  Case"
		/// </summary>
		public static string UnCamel(this string s) {
			return Regex.Replace(s, "([A-Z])(?=[a-z0-9])", " $1").Trim();
		}
	}

	public class CheckException : ApplicationException {

		public CheckException(string message)
			: this(null, message) {
		}

		public CheckException(string message, Exception ex)
			: this(ex, message + "\r\n") {
		}

		public CheckException(Exception ex, string message)
			: base(message, ex) {
		}

		public CheckException(string format, params object[] args)
			: this(string.Format(format, args)) {
		}

		public CheckException(Exception ex, string format, params object[] args)
			: this(ex, string.Format(format + "\r\n", args)) {
		}
	}

	/// <summary>
	/// Our own converter to/from decimal and float (and decimal? and float?).
	/// Everything is rounded to 4 decimal places to get over floating point inaccuracies.
	/// Converting to object, accepts strings, floats and ints.
	/// </summary>
	public class DecimalFormatJsonConverter : JsonConverter {
		public DecimalFormatJsonConverter() {
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
			if (value == null)
				writer.WriteValue((decimal?)null);
			else {
				if (value.GetType() == typeof(double) || value.GetType() == typeof(double?))
					writer.WriteValue(Math.Round((double)value, 4));
				else
					writer.WriteValue(Math.Round((decimal)value, 4));
			}
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
			JToken token = JToken.Load(reader);
			decimal d;
			switch (token.Type) {
				case JTokenType.Float:
				case JTokenType.Integer:
					d = token.ToObject<decimal>();
					break;
				case JTokenType.String:
					string s = token.ToObject<string>();
					d = string.IsNullOrWhiteSpace(s) ? 0 : decimal.Parse(s);
					break;
				case JTokenType.Null:
					if (objectType == typeof(decimal?) || objectType == typeof(float?)) {
						return null;
					}
					throw new JsonSerializationException("Value may not be null");
				default:
					throw new JsonSerializationException("Unexpected token type: " + token.Type.ToString());
			}
			d = Math.Round(d, 4);
			if (objectType == typeof(decimal) || objectType == typeof(decimal?)) return d;
			return (double)d;
		}

		public override bool CanConvert(Type objectType) {
			return objectType == typeof(decimal) || objectType == typeof(double) || objectType == typeof(decimal?) || objectType == typeof(double?);
		}
	}
}
