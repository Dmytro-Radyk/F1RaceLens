import { httpResource } from '@angular/common/http';
import { Service } from '@angular/core';
export interface NewsArticle {
  readonly title: string;
  readonly url: string;
  readonly publishedAt: string;
  readonly imageUrl?: string | null;
}
export interface NewsFeed {
  readonly source: string;
  readonly sourceUrl: string;
  readonly language: string;
  readonly updatedAt: string;
  readonly isStale: boolean;
  readonly articles: readonly NewsArticle[];
}
@Service()
export class NewsService {
  readonly feed = httpResource<NewsFeed>(() => '/api/news');
}
