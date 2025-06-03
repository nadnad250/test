#!/usr/bin/env python3
"""
LANCEUR PRINCIPAL - Bot de Trading PPO
Version corrigée avec gestion d'erreurs améliorée
"""

import sys
import os
from pathlib import Path

# Configuration des chemins
def setup_paths():
    """Configure tous les chemins nécessaires"""
    current_dir = Path(__file__).parent.absolute()
    src_dir = current_dir / "src"
    
    # Ajouter les chemins
    paths_to_add = [str(current_dir), str(src_dir)]
    for path in paths_to_add:
        if path not in sys.path:
            sys.path.insert(0, path)
    
    return current_dir, src_dir

# Configurer les chemins avant les imports
BASE_DIR, SRC_DIR = setup_paths()

try:
    import tkinter as tk
    from tkinter import ttk, messagebox
    import threading
    import time
    from datetime import datetime
    import logging
    import traceback
    
    # Configuration du logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        handlers=[
            logging.FileHandler('ppo_trading_bot.log'),
            logging.StreamHandler()
        ]
    )
    logger = logging.getLogger(__name__)
    
    print("✅ Imports de base réussis!")
    
except Exception as e:
    print(f"❌ Erreur d'import de base: {e}")
    sys.exit(1)

try:
    # Imports du système de trading
    import numpy as np
    import pandas as pd
    import gymnasium as gym
    from stable_baselines3 import PPO
    from stable_baselines3.common.vec_env import DummyVecEnv
    from stable_baselines3.common.monitor import Monitor
    
    print("✅ Imports des librairies ML réussis!")
    
except Exception as e:
    print(f"❌ Erreur d'import des librairies ML: {e}")
    print("Veuillez installer les dépendances avec: pip install stable-baselines3 gymnasium numpy pandas")
    sys.exit(1)

# Configuration par défaut améliorée
DEFAULT_CONFIG = {
    'ppo': {
        'policy': 'MlpPolicy',
        'learning_rate': 3e-4,
        'n_steps': 1024,  # Réduit pour éviter les erreurs mémoire
        'batch_size': 64,
        'n_epochs': 10,
        'gamma': 0.99,
        'gae_lambda': 0.95,
        'clip_range': 0.2,
        'ent_coef': 0.01,
        'vf_coef': 0.5,
        'max_grad_norm': 0.5,
        'use_sde': False,
        'policy_kwargs': {
            'net_arch': [128, 128],
            'activation_fn': 'tanh'
        }
    },
    'training': {
        'total_timesteps': 10000,  # Réduit pour un test rapide
        'eval_freq': 1000,
        'save_freq': 2000,
        'enable_tensorboard': False,  # Désactivé par défaut
        'save_checkpoints': True,
        'checkpoint_freq': 5000
    },
    'environment': {
        'initial_balance': 10000.0,
        'transaction_cost_pct': 0.0005,
        'window_size': 20,
        'reward_style': 'balanced'
    },
    'model_dir': 'models',
    'log_dir': 'logs'
}

class TradingEnvironmentSimple(gym.Env):
    """Environnement de trading simplifié pour test"""
    
    def __init__(self, data_size=1000):
        super().__init__()
        
        # Espaces d'action et d'observation simples
        self.action_space = gym.spaces.Discrete(3)  # 0: Hold, 1: Buy, 2: Sell
        self.observation_space = gym.spaces.Box(
            low=-np.inf, high=np.inf, shape=(10,), dtype=np.float32
        )
        
        # Données simulées
        self.data_size = data_size
        self.current_step = 0
        self.initial_balance = 10000.0
        self.balance = self.initial_balance
        self.position = 0
        self.net_worth = self.initial_balance
        
        # Générer des données de prix simulées
        self.prices = self._generate_price_data()
        
        logger.info(f"Environnement de trading initialisé avec {data_size} points de données")
    
    def _generate_price_data(self):
        """Génère des données de prix simulées"""
        np.random.seed(42)
        prices = [100.0]
        for _ in range(self.data_size):
            change = np.random.normal(0, 0.02)  # 2% volatilité
            new_price = prices[-1] * (1 + change)
            prices.append(max(new_price, 1.0))  # Prix minimum de 1
        return np.array(prices)
    
    def reset(self, seed=None, options=None):
        """Reset l'environnement"""
        if seed is not None:
            np.random.seed(seed)
        
        self.current_step = 0
        self.balance = self.initial_balance
        self.position = 0
        self.net_worth = self.initial_balance
        
        observation = self._get_observation()
        info = {'net_worth': self.net_worth}
        
        return observation, info
    
    def step(self, action):
        """Exécute une action"""
        if self.current_step >= len(self.prices) - 1:
            # Fin de l'épisode
            observation = self._get_observation()
            reward = 0
            terminated = True
            truncated = False
            info = {'net_worth': self.net_worth, 'episode': {'r': self._calculate_total_reward()}}
            return observation, reward, terminated, truncated, info
        
        current_price = self.prices[self.current_step]
        
        # Exécuter l'action
        reward = 0
        if action == 1 and self.balance > 0:  # Buy
            shares_to_buy = self.balance / current_price
            self.position += shares_to_buy
            self.balance = 0
            reward = 0.1  # Petit bonus pour trader
            
        elif action == 2 and self.position > 0:  # Sell
            self.balance += self.position * current_price
            self.position = 0
            reward = 0.1  # Petit bonus pour trader
        
        # Calculer la valeur nette
        self.net_worth = self.balance + self.position * current_price
        
        # Récompense basée sur le changement de valeur nette
        if self.current_step > 0:
            prev_net_worth = getattr(self, '_prev_net_worth', self.initial_balance)
            reward += (self.net_worth - prev_net_worth) / prev_net_worth * 100
        
        self._prev_net_worth = self.net_worth
        self.current_step += 1
        
        observation = self._get_observation()
        terminated = False
        truncated = self.current_step >= len(self.prices) - 1
        info = {'net_worth': self.net_worth}
        
        return observation, reward, terminated, truncated, info
    
    def _get_observation(self):
        """Obtient l'observation actuelle"""
        if self.current_step >= len(self.prices):
            self.current_step = len(self.prices) - 1
        
        current_price = self.prices[self.current_step]
        
        # Observation simple: [prix_actuel, solde_normalisé, position_normalisée, ...]
        obs = np.array([
            current_price / 100.0,  # Prix normalisé
            self.balance / self.initial_balance,  # Solde normalisé
            self.position,  # Position
            self.net_worth / self.initial_balance,  # Valeur nette normalisée
            self.current_step / len(self.prices),  # Progression
            0, 0, 0, 0, 0  # Padding pour arriver à 10 features
        ], dtype=np.float32)
        
        return obs
    
    def _calculate_total_reward(self):
        """Calcule la récompense totale de l'épisode"""
        return (self.net_worth - self.initial_balance) / self.initial_balance * 100

class PPOTradingBot:
    """Bot de trading PPO avec configuration robuste"""
    
    def __init__(self, config=None):
        self.config = config or DEFAULT_CONFIG.copy()
        self.env = None
        self.model = None
        self.is_trained = False
        
        logger.info("Bot PPO initialisé avec la configuration par défaut")
    
    def setup_environment(self):
        """Configure l'environnement de trading"""
        try:
            self.env = TradingEnvironmentSimple()
            logger.info("✅ Environnement configuré avec succès")
            return True
        except Exception as e:
            logger.error(f"❌ Erreur configuration environnement: {e}")
            logger.error(traceback.format_exc())
            return False
    
    def setup_ppo_model(self):
        """Configure le modèle PPO avec gestion d'erreurs robuste"""
        try:
            if self.env is None:
                raise ValueError("Environnement non configuré")
            
            # Créer l'environnement vectorisé
            vec_env = DummyVecEnv([lambda: Monitor(self.env)])
            
            # Configuration PPO
            ppo_config = self.config['ppo'].copy()
            
            # Corriger les paramètres de policy_kwargs
            policy_kwargs = ppo_config.get('policy_kwargs', {})
            if 'activation_fn' in policy_kwargs:
                if policy_kwargs['activation_fn'] == 'tanh':
                    import torch.nn as nn
                    policy_kwargs['activation_fn'] = nn.Tanh
                elif policy_kwargs['activation_fn'] == 'relu':
                    import torch.nn as nn
                    policy_kwargs['activation_fn'] = nn.ReLU
            
            # Créer le modèle PPO
            self.model = PPO(
                policy=ppo_config.get('policy', 'MlpPolicy'),
                env=vec_env,
                learning_rate=ppo_config.get('learning_rate', 3e-4),
                n_steps=ppo_config.get('n_steps', 1024),
                batch_size=ppo_config.get('batch_size', 64),
                n_epochs=ppo_config.get('n_epochs', 10),
                gamma=ppo_config.get('gamma', 0.99),
                gae_lambda=ppo_config.get('gae_lambda', 0.95),
                clip_range=ppo_config.get('clip_range', 0.2),
                ent_coef=ppo_config.get('ent_coef', 0.01),
                vf_coef=ppo_config.get('vf_coef', 0.5),
                max_grad_norm=ppo_config.get('max_grad_norm', 0.5),
                use_sde=ppo_config.get('use_sde', False),
                policy_kwargs=policy_kwargs,
                verbose=1
            )
            
            logger.info("✅ Modèle PPO configuré avec succès")
            return True
            
        except Exception as e:
            logger.error(f"❌ Erreur configuration modèle PPO: {e}")
            logger.error(traceback.format_exc())
            return False
    
    def train(self, timesteps=None):
        """Entraîne le modèle PPO"""
        try:
            if self.model is None:
                raise ValueError("Modèle PPO non configuré")
            
            timesteps = timesteps or self.config['training']['total_timesteps']
            
            logger.info(f"🚀 Début de l'entraînement PPO - {timesteps} timesteps")
            
            # Entraînement
            self.model.learn(
                total_timesteps=timesteps,
                progress_bar=True
            )
            
            # Sauvegarder le modèle
            model_dir = Path(self.config.get('model_dir', 'models'))
            model_dir.mkdir(exist_ok=True)
            model_path = model_dir / 'ppo_trading_model.zip'
            self.model.save(str(model_path))
            
            self.is_trained = True
            logger.info(f"✅ Entraînement terminé - Modèle sauvé: {model_path}")
            return True
            
        except Exception as e:
            logger.error(f"❌ Erreur pendant l'entraînement: {e}")
            logger.error(traceback.format_exc())
            return False
    
    def evaluate(self, episodes=10):
        """Évalue le modèle entraîné"""
        try:
            if not self.is_trained or self.model is None:
                raise ValueError("Modèle non entraîné")
            
            total_rewards = []
            total_profits = []
            
            for episode in range(episodes):
                obs, _ = self.env.reset()
                episode_reward = 0
                done = False
                
                while not done:
                    action, _ = self.model.predict(obs, deterministic=True)
                    obs, reward, terminated, truncated, info = self.env.step(action)
                    done = terminated or truncated
                    episode_reward += reward
                
                total_rewards.append(episode_reward)
                profit_pct = (info['net_worth'] - self.env.initial_balance) / self.env.initial_balance * 100
                total_profits.append(profit_pct)
                
                logger.info(f"Épisode {episode + 1}: Reward={episode_reward:.2f}, Profit={profit_pct:.2f}%")
            
            mean_reward = np.mean(total_rewards)
            mean_profit = np.mean(total_profits)
            
            logger.info(f"📊 Évaluation terminée:")
            logger.info(f"   Reward moyen: {mean_reward:.2f}")
            logger.info(f"   Profit moyen: {mean_profit:.2f}%")
            
            return {
                'mean_reward': mean_reward,
                'mean_profit': mean_profit,
                'all_rewards': total_rewards,
                'all_profits': total_profits
            }
            
        except Exception as e:
            logger.error(f"❌ Erreur pendant l'évaluation: {e}")
            logger.error(traceback.format_exc())
            return None

class TradingGUI:
    """Interface graphique pour le bot de trading"""
    
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("🤖 Bot de Trading PPO - Interface Principale")
        self.root.geometry("800x600")
        
        self.bot = PPOTradingBot()
        self.setup_ui()
        
        logger.info("Interface graphique initialisée")
    
    def setup_ui(self):
        """Configure l'interface utilisateur"""
        # Titre principal
        title_label = tk.Label(
            self.root, 
            text="🤖 Bot de Trading PPO",
            font=("Arial", 18, "bold"),
            bg="lightblue",
            pady=10
        )
        title_label.pack(fill='x')
        
        # Zone d'état
        self.status_frame = tk.Frame(self.root)
        self.status_frame.pack(fill='x', padx=10, pady=5)
        
        self.status_label = tk.Label(
            self.status_frame,
            text="📊 Statut: Prêt à démarrer",
            font=("Arial", 12),
            anchor='w'
        )
        self.status_label.pack(fill='x')
        
        # Boutons principaux
        button_frame = tk.Frame(self.root)
        button_frame.pack(fill='x', padx=10, pady=10)
        
        self.setup_btn = tk.Button(
            button_frame,
            text="🔧 Configurer le système",
            command=self.setup_system,
            font=("Arial", 12),
            bg="lightgreen",
            pady=5
        )
        self.setup_btn.pack(fill='x', pady=2)
        
        self.train_btn = tk.Button(
            button_frame,
            text="🚀 Lancer l'entraînement",
            command=self.start_training,
            font=("Arial", 12),
            bg="orange",
            pady=5,
            state='disabled'
        )
        self.train_btn.pack(fill='x', pady=2)
        
        self.evaluate_btn = tk.Button(
            button_frame,
            text="📊 Évaluer le modèle",
            command=self.evaluate_model,
            font=("Arial", 12),
            bg="lightblue",
            pady=5,
            state='disabled'
        )
        self.evaluate_btn.pack(fill='x', pady=2)
        
        # Zone de logs
        log_frame = tk.LabelFrame(self.root, text="📋 Logs du système", font=("Arial", 11, "bold"))
        log_frame.pack(fill='both', expand=True, padx=10, pady=10)
        
        self.log_text = tk.Text(log_frame, wrap='word', font=("Courier", 10))
        scrollbar = tk.Scrollbar(log_frame, orient="vertical", command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=scrollbar.set)
        
        self.log_text.pack(side='left', fill='both', expand=True)
        scrollbar.pack(side='right', fill='y')
        
        # Zone de configuration
        config_frame = tk.LabelFrame(self.root, text="⚙️ Configuration rapide", font=("Arial", 11, "bold"))
        config_frame.pack(fill='x', padx=10, pady=(0, 10))
        
        # Timesteps d'entraînement
        tk.Label(config_frame, text="Timesteps d'entraînement:").grid(row=0, column=0, sticky='w', padx=5)
        self.timesteps_var = tk.StringVar(value="10000")
        tk.Entry(config_frame, textvariable=self.timesteps_var, width=10).grid(row=0, column=1, padx=5)
        
        # Épisodes d'évaluation
        tk.Label(config_frame, text="Épisodes d'évaluation:").grid(row=0, column=2, sticky='w', padx=5)
        self.episodes_var = tk.StringVar(value="10")
        tk.Entry(config_frame, textvariable=self.episodes_var, width=10).grid(row=0, column=3, padx=5)
        
        self.add_log("🎯 Interface prête - Cliquez sur 'Configurer le système' pour commencer")
    
    def add_log(self, message):
        """Ajoute un message aux logs"""
        timestamp = datetime.now().strftime("%H:%M:%S")
        log_message = f"[{timestamp}] {message}\n"
        self.log_text.insert(tk.END, log_message)
        self.log_text.see(tk.END)
        self.root.update()
    
    def update_status(self, status):
        """Met à jour le statut"""
        self.status_label.config(text=f"📊 Statut: {status}")
        self.root.update()
    
    def setup_system(self):
        """Configure le système de trading"""
        self.add_log("🔧 Configuration du système en cours...")
        self.update_status("Configuration en cours...")
        
        # Configuration de l'environnement
        if not self.bot.setup_environment():
            self.add_log("❌ Échec de la configuration de l'environnement")
            self.update_status("Erreur de configuration")
            return
        
        self.add_log("✅ Environnement configuré")
        
        # Configuration du modèle PPO
        if not self.bot.setup_ppo_model():
            self.add_log("❌ Échec de la configuration du modèle PPO")
            self.update_status("Erreur de configuration PPO")
            return
        
        self.add_log("✅ Modèle PPO configuré")
        self.add_log("🎯 Système prêt pour l'entraînement!")
        
        self.update_status("Système configuré - Prêt pour l'entraînement")
        self.train_btn.config(state='normal')
        self.setup_btn.config(state='disabled')
    
    def start_training(self):
        """Lance l'entraînement en arrière-plan"""
        def train_thread():
            try:
                timesteps = int(self.timesteps_var.get())
                self.add_log(f"🚀 Démarrage de l'entraînement - {timesteps} timesteps")
                self.update_status("Entraînement en cours...")
                
                if self.bot.train(timesteps):
                    self.add_log("🎉 Entraînement terminé avec succès!")
                    self.update_status("Entraînement terminé")
                    self.evaluate_btn.config(state='normal')
                    self.train_btn.config(text="🔄 Ré-entraîner")
                else:
                    self.add_log("❌ Échec de l'entraînement")
                    self.update_status("Erreur d'entraînement")
                    
            except ValueError:
                self.add_log("❌ Erreur: Veuillez entrer un nombre valide de timesteps")
            except Exception as e:
                self.add_log(f"❌ Erreur inattendue: {e}")
        
        threading.Thread(target=train_thread, daemon=True).start()
    
    def evaluate_model(self):
        """Évalue le modèle entraîné"""
        def eval_thread():
            try:
                episodes = int(self.episodes_var.get())
                self.add_log(f"📊 Démarrage de l'évaluation - {episodes} épisodes")
                self.update_status("Évaluation en cours...")
                
                results = self.bot.evaluate(episodes)
                if results:
                    self.add_log(f"✅ Évaluation terminée:")
                    self.add_log(f"   💰 Reward moyen: {results['mean_reward']:.2f}")
                    self.add_log(f"   📈 Profit moyen: {results['mean_profit']:.2f}%")
                    self.update_status("Évaluation terminée")
                else:
                    self.add_log("❌ Échec de l'évaluation")
                    self.update_status("Erreur d'évaluation")
                    
            except ValueError:
                self.add_log("❌ Erreur: Veuillez entrer un nombre valide d'épisodes")
            except Exception as e:
                self.add_log(f"❌ Erreur inattendue: {e}")
        
        threading.Thread(target=eval_thread, daemon=True).start()
    
    def run(self):
        """Lance l'interface graphique"""
        try:
            self.root.mainloop()
        except KeyboardInterrupt:
            logger.info("Arrêt demandé par l'utilisateur")
        except Exception as e:
            logger.error(f"Erreur dans l'interface: {e}")

def main():
    """Fonction principale"""
    print("=" * 70)
    print("🤖 BOT DE TRADING PPO - LANCEUR PRINCIPAL")
    print("=" * 70)
    print("📊 Fonctionnalités disponibles:")
    print("• Entraînement d'agent PPO")
    print("• Évaluation de performance")
    print("• Interface graphique conviviale")
    print("• Gestion d'erreurs robuste")
    print("=" * 70)
    
    try:
        # Lancer l'interface graphique
        gui = TradingGUI()
        print("📊 Interface lancée - utilisez la GUI pour continuer")
        gui.run()
        
    except Exception as e:
        logger.error(f"Erreur fatale: {e}")
        logger.error(traceback.format_exc())
        print(f"\n❌ Erreur fatale: {e}")
        print("Consultez le fichier 'ppo_trading_bot.log' pour plus de détails")

if __name__ == "__main__":
    main()
