import { effect, inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { LocaleService } from './locale.service';

@Injectable()
export class LocalizedTitle extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly locale = inject(LocaleService);
  private current = 'F1 Race Lens';
  constructor() {
    super();
    effect(() => {
      this.locale.language();
      this.render();
    });
  }
  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.current = this.buildTitle(snapshot) ?? 'F1 Race Lens';
    this.render();
  }
  private render(): void {
    this.title.setTitle(this.locale.text(this.current));
  }
}
