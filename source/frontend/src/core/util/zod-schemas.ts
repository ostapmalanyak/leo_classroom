import { Instant, LocalDate } from '@js-joda/core';
import { z } from 'zod';

export const LocalDateSchema = z.string().refine(
  (value) => {
    try {
      LocalDate.parse(value);

      return true;
    } catch {
      return false;
    }
  },
  { message: 'Invalid LocalDate format' }
).transform((value) => LocalDate.parse(value));

export const InstantSchema = z.string().refine(
  (value) => {
    try {
      Instant.parse(value);

      return true;
    } catch {
      return false;
    }
  },
  { message: 'Invalid Instant format' }
).transform((value) => Instant.parse(value));
