import { Component, inject } from '@angular/core';
import { NewsService } from './news.service';
import { NewsImage } from './components/news-image';
import { currentLocale } from '../../core/i18n/locale.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
export type { NewsFeed } from './news.service';
@Component({
  selector: 'app-f1-news',
  imports: [TranslatePipe, NewsImage],
  templateUrl: './f1-news.html',
  styleUrl: './f1-news.css',
})
export class F1News {
  protected readonly feed = inject(NewsService).feed;
  protected date(value: string): string {
    return new Intl.DateTimeFormat(currentLocale(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
