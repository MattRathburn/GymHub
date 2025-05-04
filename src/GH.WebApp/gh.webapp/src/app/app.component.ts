import { Component } from '@angular/core';
import { NavMenuComponent } from './nav-menu/nav-menu.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { FooterComponent } from './footer/footer.component';
import { HomeComponent } from './home/home.component';

@Component({
    selector: 'app-root',
    imports: [NavMenuComponent, DashboardComponent, FooterComponent, HomeComponent],
    templateUrl: './app.component.html',
    styleUrl: './app.component.css'
})
export class AppComponent {
  title = 'gh.webapp';
}
