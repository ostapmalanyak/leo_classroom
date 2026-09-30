# LDAP user export

Run from the repository root with `dotnet run --project source/ldap-export`, or from this directory with `dotnet run`.
The tool requires `ldapsearch` on `PATH` (`ldap-utils` on Debian/Ubuntu) and an LDAP bind account.

Copy `credentials.example.json` to `credentials.json` and enter the account's bind DN and password
there. `credentials.json` is excluded from Git. The password is written to a temporary file with
owner-only permissions and passed to `ldapsearch` by file path, not on the command line.
Optional `LDAP_HOST`, `LDAP_BASE_DN`, and `LDAP_FILTER` variables override the school directory defaults.

The tool disables LDAP server certificate verification for its `ldapsearch` invocation, so you do not
need to set `LDAPTLS_REQCERT`. This is convenient for directories using an untrusted internal certificate,
but it means LDAPS does not protect against a man-in-the-middle server; use only on a trusted network.
