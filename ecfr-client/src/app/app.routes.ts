import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'api/ecfr/agencies',
    pathMatch: 'full'
  },
  {
    path: 'api/ecfr/agencies',
    loadComponent: () => import('./components/agency-list/agency-list.component').then(m => m.AgencyListComponent)
  },
  {
    path: 'api/ecfr/agencies/:slug',
    loadComponent: () => import('./components/agency-detail/agency-detail.component').then(m => m.AgencyDetailComponent)
  },
  {
    path: '**',
    redirectTo: 'api/ecfr/agencies'
  }
];
