import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { stripTypeScriptTypes } from 'node:module';
import test from 'node:test';

// Exercise the production TypeScript without installing Angular or any npm packages.
const sourceRoot = new URL('../../frontend/src/core/auth/', import.meta.url);
const moduleUrl = source => `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const roleSource = await readFile(new URL('roles.ts', sourceRoot), 'utf8');
const roleUrl = moduleUrl(stripTypeScriptTypes(roleSource, { mode: 'transform' }));
const source = await readFile(new URL('leo-roles.ts', sourceRoot), 'utf8');
const logic = stripTypeScriptTypes(source).replace("'./roles'", JSON.stringify(roleUrl));
const { rolesFromLdapEntryDn } = await import(moduleUrl(logic));

const cases = [
  [undefined, []],
  ['', []],
  ['CN=IF001,OU=Teachers,DC=school', ['Teacher']],
  ['CN=Last\\, First, ou = teachers ,DC=school', ['Teacher']],
  ['CN=IF001+OU=Teachers,DC=school', ['Teacher']],
  ['CN=IF001,OU=TeachersAlumni,DC=school', []],
  ['CN=OU=Teachers,OU=Students,DC=school', ['Student']],
  ['CN=Name\\,OU=Teachers,OU=Students,DC=school', ['Student']],
  ['CN=Name\\+OU=Teachers,OU=Students,DC=school', ['Student']],
  ['CN="Name,OU=Teachers",OU=Students,DC=school', ['Student']],
  ['CN=IF001,OU=TestUsers,DC=school', ['Student']],
  ['CN=Name\\', []]
];
for (const [dn, expected] of cases) {
  test(`LDAP roles: ${dn}`, () => {
    assert.deepEqual(rolesFromLdapEntryDn(dn), expected);
  });
}
