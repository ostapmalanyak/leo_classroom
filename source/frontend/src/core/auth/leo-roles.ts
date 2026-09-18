import { Role } from './roles';

/**
 * Derives a user's group from their LDAP distinguished name.
 *
 * The HTL Leonding realm has no roles claim; the school's own demos key on `ldap_entry_dn` the same way.
 * Used only to render the shell before `/api/me` has answered - the backend's own derivation is what
 * actually authorises anything.
 */
export function rolesFromLdapEntryDn(distinguishedName: string | undefined): Role[] {
  if (distinguishedName === undefined) {
    return [];
  }

  const roles: Role[] = [];
  if (containsOrganisationalUnit(distinguishedName, 'TEACHERS')) {
    roles.push(Role.Teacher);
  }
  // test accounts get the least privileged role rather than one of their own
  if (containsOrganisationalUnit(distinguishedName, 'STUDENTS')
    || containsOrganisationalUnit(distinguishedName, 'TESTUSERS')) {
    roles.push(Role.Student);
  }

  return roles;
}

function containsOrganisationalUnit(dn: string, unit: string): boolean {
  let start = 0;
  let quoted = false;
  for (let i = 0; i <= dn.length; i++) {
    if (i < dn.length) {
      const character = dn[i];
      if (character === '\\') {
        i++;
        continue;
      }
      if (character === '"') {
        quoted = !quoted;
      }
      if (quoted || (character !== ',' && character !== '+')) {
        continue;
      }
    }

    const component = dn.slice(start, i).trim();
    const equals = component.indexOf('=');
    if (!quoted && equals >= 0
      && component.slice(0, equals).trim().toUpperCase() === 'OU'
      && component.slice(equals + 1).trim().toUpperCase() === unit) {
      return true;
    }
    start = i + 1;
  }

  return false;
}
