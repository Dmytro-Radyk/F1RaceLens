import { inject, Pipe, PipeTransform } from '@angular/core';
import { LocaleService } from './locale.service';

@Pipe({ name: 'translate', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly locale = inject(LocaleService);
  transform(value: unknown, parameters: readonly unknown[] = []): string {
    return this.locale.text(value, parameters);
  }
}
