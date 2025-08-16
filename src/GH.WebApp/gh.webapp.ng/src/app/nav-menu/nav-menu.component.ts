import { Component, effect, inject } from '@angular/core';
import { MatToolbar } from '@angular/material/toolbar';
import { MatButton } from '@angular/material/button';
import Keycloak from 'keycloak-js';
import { 
    HasRolesDirective,
    KEYCLOAK_EVENT_SIGNAL,
    KeycloakEventType,
    typeEventArgs,
    ReadyArgs
} from 'keycloak-angular';

@Component({
    selector: 'nav-menu',
    imports: [MatToolbar, MatButton, HasRolesDirective],
    standalone: true,
    templateUrl: './nav-menu.component.html',
    styleUrl: './nav-menu.component.scss'
})
export class NavMenuComponent {
    authenticated = false;
    keycloakStatus: string | undefined;
    private readonly keycloak = inject(Keycloak);
    private readonly keycloakSignal = inject(KEYCLOAK_EVENT_SIGNAL);

    constructor() {
        effect(() => {
            const event = this.keycloakSignal();
            
            this.keycloakStatus = event.type;
            if (event.type === KeycloakEventType.Ready) {
                this.authenticated = typeEventArgs<ReadyArgs>(event.args);
            }

            if(event.type === KeycloakEventType.AuthLogout) {
                this.authenticated = false;
            }
        });
    }

    login() {
        this.keycloak.login();
    }   

    logout() {
        this.keycloak.logout();
    }

    signup() {
        this.keycloak.register();
    }

}
