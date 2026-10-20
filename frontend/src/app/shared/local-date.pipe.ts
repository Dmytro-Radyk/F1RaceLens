import { Pipe, PipeTransform } from '@angular/core';
import { currentLocale } from '../core/i18n/locale.service';

@Pipe({ name: 'localDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private locale = '';
  private formatter: Intl.DateTimeFormat | undefined;
  transform(value: string): string {
    const locale = currentLocale();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
    if (locale !== this.locale || !this.formatter) {
      this.locale = locale;
      this.formatter = new Intl.DateTimeFormat(locale, {
        day: 'numeric',
        month: 'long',
        year: 'numeric',
      });
    }
    const [year, month, day] = value.split('-').map(Number);
    // Date-only source fields are calendar dates, not UTC instants.
    return this.formatter.format(new Date(year, month - 1, day, 12));
  }
}
